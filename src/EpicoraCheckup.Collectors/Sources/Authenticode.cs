using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace EpicoraCheckup.Collectors.Sources
{
    /// <summary>Assinatura de um executável: se é válida e quem assinou.</summary>
    public sealed class SignatureInfo
    {
        /// <summary>
        /// <c>true</c> assinatura válida, <c>false</c> ausente ou inválida, <c>null</c> quando
        /// não foi possível verificar. Nunca <c>false</c> por ignorância — é o princípio 3 do
        /// doc 01 aplicado a um campo que a Fase 5 usa para decidir o que pode ser desativado.
        /// </summary>
        public bool? Valid { get; set; }

        public string Publisher { get; set; }
    }

    /// <summary>
    /// Verificação de assinatura de arquivo, equivalente ao <c>Get-AuthenticodeSignature</c>
    /// do protótipo.
    ///
    /// **Sem consulta de revogação, e isto é requisito, não economia.** A ferramenta não abre
    /// conexão de rede (doc 01, "não abre porta de rede", "não faz telemetria"), e verificação
    /// de revogação faz requisição externa a partir da máquina do cliente. Rodamos com
    /// <c>WTD_REVOKE_NONE</c> e recuperação de URL somente do cache local: o campo responde
    /// "esta assinatura confere", não "este certificado continua válido hoje".
    /// </summary>
    public static class Authenticode
    {
        private static readonly Guid ActionGenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;
        private const uint WtdSaferFlag = 0x00000100;
        private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;

        public static SignatureInfo Check(string filePath)
        {
            var info = new SignatureInfo { Valid = null, Publisher = null };

            try
            {
                info.Valid = IsTrusted(filePath);
            }
            catch (Exception)
            {
                // Arquivo bloqueado por EDR, caminho longo, disco removido: não sabemos.
                info.Valid = null;
            }

            try
            {
                info.Publisher = SignerCommonName(filePath);
            }
            catch (Exception)
            {
                info.Publisher = null;
            }

            return info;
        }

        private static bool IsTrusted(string filePath)
        {
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));

            try
            {
                Marshal.StructureToPtr(file, filePointer, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = filePointer,
                    dwStateAction = WtdStateActionVerify,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = null,
                    dwProvFlags = WtdSaferFlag | WtdCacheOnlyUrlRetrieval,
                    dwUIContext = 0
                };

                var dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));

                try
                {
                    Marshal.StructureToPtr(data, dataPointer, false);

                    var result = WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, dataPointer);

                    // Fechar o estado é obrigatório: sem isto o provedor vaza o contexto de
                    // cada arquivo verificado, e há um por item de inicialização.
                    var opened = (WINTRUST_DATA)Marshal.PtrToStructure(dataPointer, typeof(WINTRUST_DATA));
                    opened.dwStateAction = WtdStateActionClose;
                    Marshal.StructureToPtr(opened, dataPointer, false);
                    WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, dataPointer);

                    Marshal.DestroyStructure(dataPointer, typeof(WINTRUST_DATA));

                    // Zero é confiança confirmada. Qualquer outro código — inclusive
                    // TRUST_E_NOSIGNATURE — é resposta negativa medida, não desconhecimento.
                    return result == 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPointer);
                }
            }
            finally
            {
                Marshal.DestroyStructure(filePointer, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(filePointer);
            }
        }

        /// <summary>
        /// Nome do assinante, extraído do Subject do certificado.
        ///
        /// A extração é a mesma ingenuidade do protótipo — primeiro componente separado por
        /// vírgula, sem o prefixo <c>CN=</c> — e de propósito: as duas implementações precisam
        /// produzir o MESMO texto para o mesmo binário, senão a lista de exclusões da Fase 5
        /// passa a depender de qual das duas coletou. Trocar aqui exige trocar lá (ADR-009).
        /// </summary>
        private static string SignerCommonName(string filePath)
        {
            var certificate = X509Certificate.CreateFromSignedFile(filePath);
            if (certificate == null) return null;

            var subject = certificate.Subject;
            if (string.IsNullOrWhiteSpace(subject)) return null;

            var first = subject.Split(',')[0].Trim();
            if (first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) first = first.Substring(3);

            first = first.Trim('"').Trim();

            return string.IsNullOrWhiteSpace(first) ? null : first;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(
            IntPtr window, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        /// <summary>
        /// A união de WINTRUST_DATA é declarada como um único ponteiro porque só usamos
        /// <c>WTD_CHOICE_FILE</c>. <c>cbStruct</c> leva o tamanho DESTA declaração: o provedor
        /// usa o campo para versionar, e declarar campos que não preenchemos mudaria o tamanho
        /// e o significado da chamada.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }
    }
}

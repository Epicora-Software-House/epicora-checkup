namespace EpicoraCheckup.Core.Model
{
    /// <summary>
    /// Quem, para quem, e sob qual número — os campos da tela 1.
    ///
    /// Vive em Core, e não em App, porque Reporting precisa deles para montar o documento
    /// e Reporting não pode depender de WinForms (doc 02 §2).
    /// </summary>
    public sealed class Identification
    {
        public string Technician { get; set; }

        public string Client { get; set; }

        public string Unit { get; set; }

        public string DiagnosticId { get; set; }

        /// <summary>
        /// Marcação do técnico de que o parque é corporativo.
        ///
        /// É propriedade da VISITA, não da máquina: o protótipo PowerShell já a recebe como
        /// parâmetro de invocação, ao lado de técnico e cliente. Por isso é coletada na tela
        /// 1 e persistida entre máquinas, mesmo sendo gravada dentro do bloco
        /// <c>manual</c> do JSON, que é onde o schema 1.0 a coloca.
        ///
        /// Sem ela, OS-004 fica dependendo só de a máquina estar em domínio — e escritório
        /// pequeno sem domínio é exatamente onde a edição Home aparece.
        /// </summary>
        public bool CorporateEnvironment { get; set; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Technician) &&
            !string.IsNullOrWhiteSpace(Client) &&
            !string.IsNullOrWhiteSpace(DiagnosticId);
    }

    /// <summary>
    /// Campos da tela 4. Os três primeiros são obrigatórios, e o schema exige os três com
    /// pelo menos um caractere — são o que amarra o inventário à realidade da empresa.
    /// </summary>
    public sealed class ManualData
    {
        public string MachineLabel { get; set; }

        public string Responsible { get; set; }

        public string Department { get; set; }

        public string PhysicalLocation { get; set; }

        public string AssetTag { get; set; }

        public string PhysicalCondition { get; set; }

        public string Notes { get; set; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(MachineLabel) &&
            !string.IsNullOrWhiteSpace(Responsible) &&
            !string.IsNullOrWhiteSpace(Department);
    }
}

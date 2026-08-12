using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EpicoraCheckup.Collectors.Sources
{
    /// <summary>
    /// Retrato de um conjunto de propriedades lido de uma fonte — uma instância WMI ou uma
    /// chave do registro —, já desconectado dela.
    ///
    /// **Por que copiar em vez de passear com o <c>ManagementObject</c>.** Objeto WMI é COM
    /// vivo: exige descarte e some quando o escopo morre. Tirando um retrato de cada
    /// instância assim que ela é lida, a fonte é liberada num só lugar e todo o resto do
    /// coletor vira função de dicionário — que é o que permite testar a derivação de campo
    /// sem Windows e sem WMI, num Mac, com objeto montado à mão.
    ///
    /// Os acessadores repetem a semântica de <c>Prop</c> do protótipo, e ela não é detalhe:
    /// **propriedade ausente e texto vazio viram <c>null</c>**, nunca string vazia. Campo
    /// vazio virando "" no JSON destrói a análise no consolidador (doc 02 §5).
    /// </summary>
    public sealed class PropertyBag
    {
        private readonly IDictionary<string, object> _values;

        /// <param name="values">
        /// Dicionário INSENSÍVEL a maiúsculas, como são os nomes de propriedade em WMI e de
        /// valor no registro. Quem monta passa o comparador; copiar aqui lançaria em chave
        /// duplicada que difere só por caixa.
        /// </param>
        public PropertyBag(string className, IDictionary<string, object> values)
        {
            ClassName = className;
            _values = values ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Nome da classe (<c>__CLASS</c>). Único jeito de distinguir gatilho de tarefa agendada.</summary>
        public string ClassName { get; }

        public bool Has(string name)
        {
            return _values.ContainsKey(name) && _values[name] != null;
        }

        public object Raw(string name)
        {
            object value;
            return _values.TryGetValue(name, out value) ? value : null;
        }

        // ------------------------------------------------------------ escalares

        public string Text(string name)
        {
            var value = Raw(name);
            if (value == null) return null;

            var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>Texto com espaços das pontas removidos. Serial e part number vêm preenchidos com espaço.</summary>
        public string Trimmed(string name)
        {
            var text = Text(name);
            return text == null ? null : text.Trim();
        }

        public bool? Flag(string name)
        {
            var value = Raw(name);
            if (value == null) return null;
            if (value is bool) return (bool)value;

            // Algumas classes devolvem 0/1 onde o MOF diz boolean, e outras devolvem "TRUE".
            var text = value as string;
            if (text != null)
            {
                bool parsed;
                if (bool.TryParse(text, out parsed)) return parsed;
            }

            var number = Long(name);
            return number.HasValue ? (bool?)(number.Value != 0) : null;
        }

        public int? Int(string name)
        {
            var value = Long(name);
            if (!value.HasValue) return null;
            if (value.Value > int.MaxValue || value.Value < int.MinValue) return null;

            return (int)value.Value;
        }

        public long? Long(string name)
        {
            var value = Raw(name);
            if (value == null) return null;

            try
            {
                var text = value as string;
                if (text != null)
                {
                    if (string.IsNullOrWhiteSpace(text)) return null;

                    long parsed;
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                        ? (long?)parsed
                        : null;
                }

                if (value is bool) return (bool)value ? 1L : 0L;

                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // Valor fora de faixa ou de tipo inesperado é "não sei", nunca zero.
                return null;
            }
        }

        public double? Number(string name)
        {
            var value = Raw(name);
            if (value == null) return null;

            try
            {
                var text = value as string;
                if (text != null)
                {
                    if (string.IsNullOrWhiteSpace(text)) return null;

                    double parsed;
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                        ? (double?)parsed
                        : null;
                }

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Data em CIM_DATETIME. O <see cref="System.Management"/> entrega estas propriedades
        /// como texto <c>"20260729181835.000000-180"</c>, e não como <see cref="DateTime"/>.
        /// </summary>
        public DateTimeOffset? Moment(string name)
        {
            var value = Raw(name);
            if (value == null) return null;

            if (value is DateTime) return new DateTimeOffset((DateTime)value);
            if (value is DateTimeOffset) return (DateTimeOffset)value;

            return ParseCimDateTime(value as string);
        }

        // ------------------------------------------------------------ vetores

        public IList<string> Texts(string name)
        {
            var list = new List<string>();

            foreach (var item in Enumerate(Raw(name)))
            {
                var text = item as string ?? Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text);
            }

            return list;
        }

        public IList<int> Ints(string name)
        {
            var list = new List<int>();

            foreach (var item in Enumerate(Raw(name)))
            {
                try
                {
                    list.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    // Item ilegível não invalida os outros do vetor.
                }
            }

            return list;
        }

        /// <summary>Instâncias embutidas, como os gatilhos dentro de <c>MSFT_ScheduledTask</c>.</summary>
        public IList<PropertyBag> Embedded(string name)
        {
            var list = new List<PropertyBag>();

            foreach (var item in Enumerate(Raw(name)))
            {
                var nested = item as PropertyBag;
                if (nested != null) list.Add(nested);
            }

            return list;
        }

        private static IEnumerable<object> Enumerate(object value)
        {
            if (value == null) yield break;

            // String é IEnumerable de char: sem este desvio, um valor escalar de texto
            // viraria uma lista de letras.
            if (value is string)
            {
                yield return value;
                yield break;
            }

            var items = value as System.Collections.IEnumerable;
            if (items == null)
            {
                yield return value;
                yield break;
            }

            foreach (var item in items)
                if (item != null) yield return item;
        }

        // ------------------------------------------------------------ CIM_DATETIME

        private static readonly Regex CimDateTime = new Regex(
            @"^(?<ano>\d{4})(?<mes>\d{2})(?<dia>\d{2})(?<hora>\d{2})(?<min>\d{2})(?<seg>\d{2})" +
            @"\.(?<frac>\d{6})(?<sinal>[+\-])(?<offset>\d{3})$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Converte <c>yyyyMMddHHmmss.ffffff±UUU</c>, onde <c>UUU</c> é o deslocamento de fuso
        /// em MINUTOS — não em horas.
        ///
        /// Escrito à mão em vez de <c>ManagementDateTimeConverter.ToDateTime</c> por dois
        /// motivos: o conversor devolve hora local sem offset, e o schema exige offset
        /// explícito; e uma função pura pode ser testada fora do Windows.
        ///
        /// Data zerada (<c>00000000000000</c>) aparece em BIOS mal preenchida e vira null:
        /// é ausência de dado, não 1º de janeiro do ano zero.
        /// </summary>
        public static DateTimeOffset? ParseCimDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = CimDateTime.Match(value.Trim());
            if (!match.Success) return null;

            try
            {
                var ano = int.Parse(match.Groups["ano"].Value, CultureInfo.InvariantCulture);
                var mes = int.Parse(match.Groups["mes"].Value, CultureInfo.InvariantCulture);
                var dia = int.Parse(match.Groups["dia"].Value, CultureInfo.InvariantCulture);
                var hora = int.Parse(match.Groups["hora"].Value, CultureInfo.InvariantCulture);
                var minuto = int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
                var segundo = int.Parse(match.Groups["seg"].Value, CultureInfo.InvariantCulture);

                if (ano == 0 || mes == 0 || dia == 0) return null;

                var minutos = int.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
                if (match.Groups["sinal"].Value == "-") minutos = -minutos;

                // Fuso fora de ±14 h é lixo do firmware, e o construtor lançaria.
                if (minutos < -840 || minutos > 840) return null;

                return new DateTimeOffset(ano, mes, dia, hora, minuto, segundo,
                    TimeSpan.FromMinutes(minutos));
            }
            catch (ArgumentOutOfRangeException)
            {
                // 31 de fevereiro existe em firmware ruim. Vira null, não exceção no coletor.
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

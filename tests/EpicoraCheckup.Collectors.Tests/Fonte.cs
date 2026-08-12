using System;
using System.Collections.Generic;
using EpicoraCheckup.Collectors.Sources;

namespace EpicoraCheckup.Collectors.Tests
{
    /// <summary>
    /// Monta o retrato de uma instância como a fonte a devolveria.
    ///
    /// Os valores são declarados com os TIPOS que WMI entrega de verdade — texto onde a classe
    /// declara texto, inteiro sem sinal onde ela declara <c>uint32</c>, e data em CIM_DATETIME
    /// como texto. Simplificar isso para <c>int</c> e <c>DateTime</c> deixaria o teste verde
    /// contra um objeto que a máquina nunca produz, que é a maneira mais confortável de não
    /// testar nada.
    /// </summary>
    internal static class Fonte
    {
        internal static PropertyBag Bag(params object[] paresChaveValor)
        {
            return Classe(null, paresChaveValor);
        }

        internal static PropertyBag Classe(string nomeDaClasse, params object[] paresChaveValor)
        {
            if (paresChaveValor.Length % 2 != 0)
                throw new ArgumentException("esperava pares nome, valor", nameof(paresChaveValor));

            var valores = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < paresChaveValor.Length; i += 2)
                valores[(string)paresChaveValor[i]] = paresChaveValor[i + 1];

            return new PropertyBag(nomeDaClasse, valores);
        }

        internal static IList<PropertyBag> Lista(params PropertyBag[] itens)
        {
            return new List<PropertyBag>(itens);
        }

        internal static IList<PropertyBag> Nenhum()
        {
            return new List<PropertyBag>();
        }
    }
}

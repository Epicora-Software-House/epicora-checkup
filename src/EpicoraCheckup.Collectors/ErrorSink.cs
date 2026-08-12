using System;
using System.Collections.Generic;
using EpicoraCheckup.Core.Contracts;

namespace EpicoraCheckup.Collectors
{
    /// <summary>
    /// Onde um coletor registra o que não conseguiu ler sem deixar de produzir o resto.
    ///
    /// O protótipo engole estas falhas em <c>catch { }</c> silencioso e o campo simplesmente
    /// sai null. Aqui o null continua indo para o JSON — o que muda é que o MOTIVO fica
    /// gravado em <c>collectors[].errors</c>, que é onde se descobre, meses depois, por que
    /// uma regra resolveu Indeterminate numa máquina específica.
    ///
    /// O texto vai para o log e para o pacote de entrega interna. **Nunca para a tela do
    /// cliente:** o cliente vê "não foi possível verificar" com o motivo em linguagem de
    /// relatório, não o nome de uma classe WMI.
    /// </summary>
    public sealed class ErrorSink
    {
        private readonly List<CollectorError> _errors = new List<CollectorError>();

        public IList<CollectorError> Errors
        {
            get { return _errors; }
        }

        public void Record(string source, string message, string detail = null)
        {
            _errors.Add(new CollectorError { Source = source, Message = message, Detail = detail });
        }

        public void Record(string source, Exception exception)
        {
            if (exception == null) return;

            Record(source, exception.Message, exception.ToString());
        }

        /// <summary>
        /// Lê uma fonte que pode não existir nesta máquina, devolvendo o padrão do tipo
        /// quando falha.
        ///
        /// <see cref="OperationCanceledException"/> passa direto: cancelamento é decisão de
        /// quem opera a ferramenta, e transformá-lo em "fonte indisponível" faria a coleta
        /// cancelada produzir um relatório de aparência normal.
        /// </summary>
        public T Read<T>(string source, Func<T> read)
        {
            T value;
            TryRead(source, read, out value);
            return value;
        }

        public bool TryRead<T>(string source, Func<T> read, out T value)
        {
            try
            {
                value = read();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Record(source, exception);
                value = default(T);
                return false;
            }
        }
    }
}

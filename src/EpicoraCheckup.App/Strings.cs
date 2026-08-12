namespace EpicoraCheckup.App
{
    /// <summary>
    /// Todo texto exibido na interface, num só lugar.
    ///
    /// ADR-008: nenhuma string de UI hardcoded no meio do código. Vale por si, mesmo sem
    /// intenção de traduzir — é o que permite o comercial revisar texto sem caçar literais
    /// pelo código, e o princípio 6 do doc 01 exige que todo texto exibido seja escrito
    /// para quem vai assinar a proposta, não para quem escreve o código.
    ///
    /// pt-BR apenas na v1. Sem infraestrutura de localização, que seria complexidade paga
    /// sem uso.
    /// </summary>
    internal static class Strings
    {
        internal const string AppName = "Epicora Checkup";

        // ---------------------------------------------------------------- comum

        internal const string BotaoVoltar = "Voltar";
        internal const string BotaoAvancar = "Avançar";
        internal const string BotaoCancelar = "Cancelar";

        internal const string CampoObrigatorio = "Preencha os campos obrigatórios para avançar.";

        // ---------------------------------------------------------------- modo demonstração

        internal const string DemonstracaoFaixa =
            "MODO DEMONSTRAÇÃO — dados de arquivo de exemplo. Nada é coletado desta máquina e nenhum arquivo é gravado.";

        internal const string DemonstracaoTela7 =
            "Nenhum arquivo foi gravado porque a ferramenta está em modo demonstração.\n\n" +
            "Este modo existe para revisar as telas e os textos do relatório antes de os coletores " +
            "estarem prontos. Um relatório de demonstração nunca pode ser entregue a um cliente, " +
            "e é por isso que ele não gera arquivo.";

        internal const string NenhumColetor =
            "Nenhuma etapa de coleta foi montada para esta execução.";

        // ---------------------------------------------------------------- tela 1

        internal const string Tela1Titulo = "Identificação do diagnóstico";

        internal const string Tela1Tecnico = "Técnico responsável";
        internal const string Tela1Cliente = "Empresa cliente";
        internal const string Tela1Unidade = "Unidade / filial";
        internal const string Tela1Diagnostico = "Número do diagnóstico";

        /// <summary>Aviso permanente e não dispensável da tela 1 (doc 01 §5).</summary>
        internal const string Tela1AvisoPrivacidade =
            "Esta ferramenta lê apenas metadados de hardware, software e configuração. " +
            "Não acessa conteúdo de arquivos, e-mails, mensagens ou histórico de navegação.";

        internal const string Tela1Iniciar = "Iniciar coleta";

        internal const string Tela1Elevado =
            "Executando com privilégio de administrador. Todas as fontes de dados serão consultadas.";

        internal const string Tela1NaoElevado =
            "Executando sem privilégio de administrador. TPM, BitLocker e verificação SMART do disco " +
            "não poderão ser lidos e aparecerão no relatório como \"não foi possível verificar\". " +
            "Todo o resto é coletado normalmente.";

        // ---------------------------------------------------------------- tela 2

        internal const string Tela2Titulo = "Coletando dados desta máquina";

        internal const string EtapaPendente = "Pendente";
        internal const string EtapaExecutando = "Executando";
        internal const string EtapaConcluido = "Concluído";
        internal const string EtapaIgnorado = "Ignorado";
        internal const string EtapaFalhou = "Falhou";

        internal const string Tela2SemPrivilegio = "sem privilégio de administrador";
        internal const string Tela2TempoLimite = "tempo limite excedido";

        internal const string Tela2Decorrido = "Decorrido: {0:0.0} s";
        internal const string Tela2Concluida = "Coleta concluída em {0:0.0} segundos.";
        internal const string Tela2NuncaInterrompe =
            "Etapa que falha não interrompe a coleta. O que não pudermos verificar aparece no " +
            "relatório como \"não foi possível verificar\", nunca como problema encontrado.";

        // ---------------------------------------------------------------- tela 3

        internal const string Tela3Titulo = "Riscos e pontos de atenção";

        internal const string Tela3Score = "Índice de saúde";
        internal const string Tela3Veredito = "Veredito";

        internal const string FaixaVerde = "Verde";
        internal const string FaixaAmarela = "Amarelo";
        internal const string FaixaVermelha = "Vermelho";

        internal const string VereditoManter = "Manter";
        internal const string VereditoUpgrade = "Fazer upgrade";
        internal const string VereditoSubstituir = "Substituir";

        internal const string SeveridadeCritico = "Crítico";
        internal const string SeveridadeAlto = "Alto";
        internal const string SeveridadeMedio = "Médio";
        internal const string SeveridadeBaixo = "Baixo";
        internal const string SeveridadeInfo = "Informativo";

        internal const string Tela3AcaoRecomendada = "Ação recomendada:";
        internal const string Tela3SemAchados = "Nenhum risco identificado nesta máquina.";

        internal const string Tela3NaoVerificado = "Não foi possível verificar";
        internal const string Tela3NaoVerificadoExplicacao =
            "Os itens abaixo não pudemos avaliar. Não são problemas encontrados — são perguntas " +
            "que ficaram sem resposta, e o motivo de cada uma está declarado.";

        internal const string Tela3MarcarFalsoPositivo = "Marcar como falso positivo";
        internal const string Tela3FalsoPositivoTitulo = "Marcar achado como falso positivo";
        internal const string Tela3FalsoPositivoJustificativa =
            "Justifique. O texto vai para o relatório e alimenta a correção da regra:";
        internal const string Tela3FalsoPositivoMarcado = "Marcado como falso positivo";
        internal const string Tela3FalsoPositivoExigeJustificativa =
            "A justificativa é obrigatória: é ela que permite corrigir a regra depois.";

        // ---------------------------------------------------------------- tela 4

        internal const string Tela4Titulo = "Dados da máquina no padrão do cliente";

        internal const string Tela4Explicacao =
            "Estes campos amarram o inventário à realidade da empresa. Sem eles o relatório é " +
            "uma lista de números sem dono.";

        internal const string Tela4Etiqueta = "Identificação da máquina no padrão do cliente";
        internal const string Tela4Responsavel = "Responsável / usuário principal";
        internal const string Tela4Setor = "Setor";
        internal const string Tela4Localizacao = "Localização física";
        internal const string Tela4Patrimonio = "Etiqueta de patrimônio";
        internal const string Tela4CondicaoFisica = "Situação física observada";
        internal const string Tela4CondicaoFisicaDica =
            "Limpeza interna, ruído de ventoinha, teclado, tela, cabos.";
        internal const string Tela4Observacoes = "Observações livres";
        internal const string Tela4ObservacoesDica =
            "O que o usuário relatou, em palavras dele.";

        // ---------------------------------------------------------------- tela 7

        internal const string Tela7Titulo = "Arquivos gerados";

        internal const string Tela7NadaEnviado =
            "Nada foi enviado para nenhum servidor. Os arquivos estão apenas nesta máquina, " +
            "e é o técnico que os leva.";

        internal const string Tela7FalhaAoGravar =
            "Não foi possível gravar os arquivos desta máquina.\n\n{0}\n\n" +
            "A coleta em si deu certo. Verifique se a pasta ao lado do executável aceita " +
            "gravação — pen drive protegido, pasta de rede e antivírus bloqueando escrita são " +
            "as causas comuns — e rode de novo nesta máquina.";

        internal const string Tela7Aviso = "Atenção: {0}";

        internal const string Tela7AbrirPasta = "Abrir pasta";
        internal const string Tela7Encerrar = "Encerrar";
        internal const string Tela7FalhaAoAbrir = "Não foi possível abrir a pasta: {0}";

        // ---------------------------------------------------------------- erro

        internal const string ErroInesperado =
            "A ferramenta encontrou um erro que não esperava.\n\n{0}\n\n" +
            "O log da execução tem o detalhe completo.";
    }
}

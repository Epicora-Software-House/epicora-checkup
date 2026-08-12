# ADR-013 — Executável único: assemblies mesclados e matriz embutida

**Estado:** Aceita
**Data:** 2026-08-12
**Decisão de:** Gabriel Oss
**Referência:** doc 01 §4 (arquivo único) e §8 (saídas); doc 02 §3.5 (matriz declarativa), §8.1 (URL estável) e §8.4 (SmartScreen); [ADR-002](002-distribuicao-do-binario.md), [ADR-003](003-certificado-de-assinatura.md)

## Contexto

O doc 01 §4 exige **executável único, sem instalação**. Até aqui o pacote era uma pasta com seis arquivos: o `.exe`, quatro DLLs do próprio projeto, o `Newtonsoft.Json.dll`, um `.exe.config` — e, ao lado, a pasta `rules/` com a matriz.

Duas exigências se contradizem, e o comentário no `RulesLocator` registrava isso desde a Fase 2:

> O doc 01 §4 exige executável único e o doc 02 §3.5 exige que mudar uma regra não obrigue a recompilar. Ler de pasta externa atende o segundo e contraria o primeiro. Embutir como recurso faz o inverso. **Não é decisão para tomar dentro deste arquivo.**

O que forçou a decisão agora foi operacional: a validação de campo vai ser conduzida **por telefone com o técnico**, e cada arquivo a mais é um passo a mais para dar errado do outro lado da linha. "Baixa esse link e roda" só funciona com um arquivo.

## Decisão

**Um arquivo, o `EpicoraCheckup.exe`, com tudo dentro.**

1. **Assemblies mesclados com ILRepack**, no `publish` e só em Release. Os quatro projetos e o `Newtonsoft.Json` entram no executável, com `Internalize` — nada referencia este binário, então não há API pública a preservar, e internalizar evita colisão com um Newtonsoft que exista no GAC da máquina do cliente.

2. **Matriz de regras embutida como recurso**, com **precedência para uma pasta `rules/` ao lado do executável**, quando ela existir. É o que faz as duas exigências coexistirem: o arquivo único roda sozinho, e quem precisa trocar texto sem recompilar põe a pasta ao lado.

3. **A configuração de DPI migra para o `app.manifest`** e o `.exe.config` deixa de existir.

## Motivo de cada parte

**Por que embutir e não só copiar a pasta junto.** Um `.exe` que não avalia nada sem uma pasta ao lado não é executável único — é um instalador manual. E o modo de falhar é o pior possível: alguém copia só o `.exe` para a máquina do cliente, a ferramenta abre, coleta, e morre na hora de avaliar.

**Por que a pasta externa vence a embutida.** A alternativa seria uma opção de linha de comando, mais explícita. Perde para a simplicidade de "põe a pasta ao lado" no caso de uso real: o comercial revisando texto, sem passar por build nem por parâmetro. O risco — pasta velha esquecida ao lado do executável novo — é mitigado registrando no log **de onde a matriz veio**, em toda execução.

**Por que a busca deixou de subir a árvore de diretórios.** Antes o `RulesLocator` subia até achar `rules/`, para funcionar em desenvolvimento com o binário em `bin/Release`. Com a matriz embutida a partir da mesma pasta, o build de desenvolvimento já carrega a matriz atual, e subir a árvore vira só risco: uma pasta `rules/` num nível acima passaria a valer sem ninguém pedir. Agora olha exatamente um lugar — ao lado do executável.

**Por que o DPI mudou de arquivo.** A documentação da Microsoft desaconselha declarar DPI no manifest para .NET Framework 4.7+, com um motivo específico: o manifest *"overrides settings defined on the app.config file"*. A ressalva pressupõe que exista um `App.config`. A partir daqui não existe — e o manifest passa a ser a única fonte, sem sobrescrever coisa nenhuma. Sem essa migração, a janela sai borrada em tela com escala de 125%, que é a configuração de fábrica de boa parte dos notebooks que a Epicora visita.

## Consequências, e elas são reais

1. **Depurar um executável mesclado é pior.** Por isso o `build` normal continua produzindo as DLLs soltas; só o `publish` em Release mescla. Quem desenvolve não paga o custo.

2. **Trocar um texto de regra passa a exigir build — ou a pasta ao lado.** É a contrapartida direta de embutir. O caminho sem build existe e está documentado no `LEIA-ME.txt` do pacote, mas deixou de ser o caminho padrão.

3. **A pasta externa é um vetor de divergência.** Duas máquinas podem rodar o mesmo executável com matrizes diferentes. O log registra a origem em toda execução, e é o que permite responder à primeira pergunta de um achado contestado: qual matriz produziu este número.

4. **Sem `.exe.config`, uma máquina sem nenhum runtime .NET 4.x dá erro genérico** em vez da mensagem específica de runtime ausente. Windows 10 e 11 trazem o 4.8 de fábrica, e são o alvo declarado no critério de aceite 1 — o caso é teórico, e custa um arquivo permanente ao lado do executável.

5. **O binário passa de 60 KB para cerca de 1 MB.** Irrelevante para download, e é o preço de não ter dependência.

## O que este ADR NÃO faz

**Não resolve assinatura de código.** O [ADR-003](003-certificado-de-assinatura.md) segue valendo: sem certificado, o SmartScreen avisa em toda máquina. O que este ADR acrescenta é o **SHA-256 publicado ao lado do binário** (doc 02 §8.4, mitigação 2), que é o que permite conferir a origem do arquivo sem certificado.

**Não implementa a verificação de versão** do doc 02 §8.3. Ela exige que a ferramenta faça uma requisição externa a partir da máquina do cliente, e o README afirma, em primeira linha, que a ferramenta *não abre porta de rede e não faz telemetria*. As duas coisas podem ser conciliadas — consulta a um arquivo estático, sem enviar dado nenhum —, mas a redação do que se promete ao cliente precisa mudar junto. **É decisão de produto, e fica pendente.**

## Revisão

Reabrir se o pacote precisar voltar a ter arquivos ao lado — por exemplo se a Fase 5 exigir binário auxiliar que não possa ser embutido, ou se algum EDR passar a bloquear especificamente executável com assemblies mesclados, comportamento que existe em alguns produtos e que só o campo revela.

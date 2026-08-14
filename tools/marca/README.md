# Ativos de marca

Como os binários de `src/EpicoraCheckup.App/Marca/` e `src/EpicoraCheckup.Reporting/Marca/`
foram derivados. Existe pelo mesmo motivo de `tool.rulesVersion` existir: binário comitado sem
procedência é binário que ninguém consegue conferir nem regerar dois anos depois.

As decisões de por que cada formato é esse estão no
[ADR-016](../../docs/adr/016-identidade-visual.md). Aqui está só o caminho de volta ao original.

## De onde vem cada coisa

| Ativo | Origem |
|---|---|
| `logo-branco.svg` | Drive da marca › `LOGOTIPO/SVG/Epicora_horizontal(roxa).svg`, com o `fill` trocado para branco. É o mesmo arquivo que os decks comerciais embutem em base64. |
| `logo-branco.png`, `logo-roxo.svg`, `epicora.ico` | Gerados por `gerar.py` a partir do SVG acima. |
| `Alexandria-*.ttf`, `Alexandria-SemiBold.woff2` | Instâncias estáticas geradas por `fontes.py` a partir da fonte variável de [google/fonts](https://github.com/google/fonts/tree/main/ofl/alexandria), sob SIL OFL 1.1. |
| `OFL.txt` | A licença, do mesmo repositório. Viaja junto por exigência dela. |

> A pasta `Fontes/` do Drive traz a mesma Alexandria variável, numa versão ligeiramente
> anterior. Os scripts usam a do `google/fonts` porque é a origem upstream do arquivo do kit e
> tem endereço estável — se a diferença de versão passar a importar, é trocar a URL em
> `fontes.py`.

## Como regerar

Precisa de Python 3 e das dependências abaixo. **Não** faz parte do build: os ativos são
comitados, e o CI não roda nada disto.

```sh
python3 -m venv .venv
.venv/bin/pip install pillow cairosvg fonttools brotli

# logotipos e ícone
.venv/bin/python gerar.py

# instâncias estáticas da Alexandria (baixa a variável do google/fonts)
curl -sSL "https://raw.githubusercontent.com/google/fonts/main/ofl/alexandria/Alexandria%5Bwght%5D.ttf" -o Alexandria-var.ttf
curl -sSL "https://raw.githubusercontent.com/google/fonts/main/ofl/alexandria/OFL.txt" -o OFL.txt
.venv/bin/python fontes.py
```

`fontes.py` confere sozinho, e imprime, que os dois cortes saíram com o nome de família
esperado e com a acentuação pt-BR completa. Se a checagem disser que falta alguma coisa, o
subconjunto ficou estreito demais e o executável desenharia quadrado no lugar do caractere.

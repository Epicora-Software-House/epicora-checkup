"""Gera as instancias estaticas da Alexandria que o executavel embute.

Por que estatica e nao a variavel: o GDI do Windows nao interpola eixo de peso.
Carregar a variavel entrega sempre a instancia padrao, e pedir Bold faria o GDI+
sintetizar (ou lancar) em vez de usar o desenho certo.

Por que dois arquivos com FAMILIAS diferentes: pedir FontStyle.Bold a uma familia
privada que nao tem o corte lanca ArgumentException. Com "Alexandria" e
"Alexandria SemiBold" separadas, todo Font pedido e Regular e nada e sintetizado.

Subconjunto latin + latin-ext: a fonte so pinta texto de Strings.cs, que e pt-BR
por ADR-008. Nome de cliente digitado pelo tecnico continua no Segoe UI, entao
nao ha como um caractere fora do subconjunto virar quadrado na tela.
"""
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

# latin basico + suplemento + extendido A + pontuacao geral + moedas + simbolos usados
UNICODES = "U+0020-007E,U+00A0-00FF,U+0100-017F,U+2000-206F,U+20A0-20BF,U+2122,U+2212"

CORTES = [(400, "Alexandria-Regular.ttf"), (600, "Alexandria-SemiBold.ttf")]

for peso, saida in CORTES:
    fonte = TTFont("Alexandria-var.ttf")
    estatica = instancer.instantiateVariableFont(fonte, {"wght": peso}, updateFontNames=True)
    estatica.save("_cheia.ttf")

    opcoes = subset.Options()
    opcoes.layout_features = ["kern", "liga", "ccmp", "locl", "mark", "mkmk"]
    opcoes.name_IDs = ["*"]
    opcoes.notdef_outline = True
    opcoes.recalc_bounds = True

    fonte = subset.load_font("_cheia.ttf", opcoes)
    subsetador = subset.Subsetter(options=opcoes)
    subsetador.populate(unicodes=subset.parse_unicodes(UNICODES))
    subsetador.subset(fonte)
    subset.save_font(fonte, saida, opcoes)

    conferir = TTFont(saida)
    cmap = set(conferir.getBestCmap())
    faltam = [c for c in "áàâãéêíóôõúüçÁÀÂÃÉÊÍÓÔÕÚÜÇºª—·"if ord(c) not in cmap]
    print(
        saida,
        "| familia:", conferir["name"].getDebugName(1),
        "| corte:", conferir["name"].getDebugName(2),
        "| glifos:", len(cmap),
        "| faltando:", "".join(faltam) or "nada",
    )

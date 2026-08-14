"""Gera os ativos de marca do Epicora Checkup a partir do SVG oficial.

Entrada: logo-branco.svg (extraido do deck, mesmo arquivo do Drive com fill trocado).
Saidas:  logo-branco.png  -> faixa roxa do WinForms
         logo-roxo.png    -> uso claro, se precisar
         epicora.ico      -> icone do executavel (isotipo roxo)

O <style>/class do SVG vira atributo fill direto: o suporte a CSS do cairosvg
depende de tinycss2 e falha em silencio deixando tudo preto.
"""
import io
import re

import cairosvg
from PIL import Image

FONTE = open("logo-branco.svg", encoding="utf-8").read()


def pinta(svg, cor):
    """Troca o mecanismo de CSS por fill explicito em cada forma."""
    svg = re.sub(r"<defs>.*?</defs>", "", svg, flags=re.S)
    return svg.replace('class="cls-1"', 'fill="%s"' % cor)


def so_isotipo(svg):
    """Mantem apenas o primeiro <g> — o simbolo, sem a palavra."""
    grupos = re.findall(r"<g>.*?</g>", svg, flags=re.S)
    cabeca = svg[: svg.index("<g>")]
    return cabeca + grupos[0] + "</svg>"


def rasteriza(svg, largura):
    png = cairosvg.svg2png(bytestring=svg.encode("utf-8"), output_width=largura)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def apara(img):
    """Corta a margem transparente que sobra do viewBox."""
    caixa = img.getbbox()
    return img.crop(caixa)


ROXO = "#6100ff"

# Horizontal branco, para a faixa roxa. 1200 de largura da folga ate 300% de DPI.
branco = apara(rasteriza(pinta(FONTE, "#ffffff"), 1200))
branco.save("logo-branco.png", optimize=True)

# Horizontal roxo, para fundo claro.
roxo = apara(rasteriza(pinta(FONTE, ROXO), 1200))
roxo.save("logo-roxo.png", optimize=True)

# Icone: isotipo roxo, quadrado, com respiro de 8% dos lados.
#
# A largura pedida vale para o viewBox INTEIRO, e o isotipo ocupa menos de um quarto
# dele. Rasterizar em 1024 daria um simbolo de ~230 px, que o .ico de 256 ampliaria.
iso = apara(rasteriza(so_isotipo(pinta(FONTE, ROXO)), 5000))
lado = int(max(iso.size) * 1.16)
tela = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
tela.paste(iso, ((lado - iso.width) // 2, (lado - iso.height) // 2), iso)
tela.save("epicora.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
tela.resize((256, 256), Image.LANCZOS).save("isotipo-roxo.png", optimize=True)

for nome, img in [("logo-branco.png", branco), ("logo-roxo.png", roxo), ("isotipo", tela)]:
    print(nome, img.size)

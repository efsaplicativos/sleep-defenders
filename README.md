# sleep-defenders

**Brinquedos vs. Monstros do Escuro**: tower defense em 3D (Unity 6 + URP) no estilo "plantas contra zumbis".

Os brinquedos, sobre o tapete do quarto de uma criança, defendem a cama dos monstros das sombras.

- **Defensores:** Abajur (gera Luz), Robô Laser (atira na própria faixa), Urso de Pelúcia (bloqueia) e Carrinho RC (limpa a faixa inteira).
- **Inimigos:** Sombra Rastejante, Monstro do Armário, Pesadelo Alado e Devorador de Luz (caça os abajures).
- **Recurso:** Luz, gerada pelos abajures (cada novo custa mais) ou pelo luar que cai do teto.
- **Luz como território:** no escuro os Robôs atiram mais devagar; na luz dos abajures as sombras ficam lentas. As noites vão escurecendo e trazem apagões.
- **Rajadas:** os monstros chegam em grupos e em várias faixas ao mesmo tempo.
- **Campanha:** 5 noites com dificuldade crescente. Cada noite libera brinquedos, monstros e regras novas, e o progresso fica salvo.
- **Última defesa:** um Ratinho de Corda em cada faixa salva a cama uma vez por noite (só nas noites 1–3).
- **Arte:** modelos 3D gerados com a Tripo AI; texturas, ícones e tela de título com Nano Banana (Gemini).

## Como jogar

1. Abra a pasta do projeto no **Unity 6000.3** pelo Unity Hub.
2. Abra a cena pelo menu **Sleep Defenders → Abrir Cena do Jogo**.
3. Aperte **Play**, escolha a noite e clique em **Começar Noite N**.

Controles:

- `1`, `2`, `3`... ou clique no card: escolhe o brinquedo.
- Clique no tapete: posiciona o brinquedo.
- Clique nos orbes: coleta Luz.
- `R`: guarda (remove) um brinquedo.
- `Esc` ou botão direito: cancela.

O guia completo fica em [`Assets/SleepDefenders/LEIA-ME.md`](Assets/SleepDefenders/LEIA-ME.md). Ele traz arquitetura, prefabs, tags e layers, balanceamento, testes e como gerar a arte de novo.

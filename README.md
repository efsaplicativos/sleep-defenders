# sleep-defenders

**Brinquedos vs. Monstros do Escuro**: tower defense em 3D (Unity 6 + URP) no estilo "plantas contra zumbis".

Os brinquedos, sobre o tapete do quarto de uma criança, defendem a cama dos monstros das sombras.

- **Defensores:** Abajur (gera Luz), Robô Laser (atira na própria faixa), Urso de Pelúcia (bloqueia), Carrinho RC (limpa a faixa inteira), Caixinha de Música (deixa os monstros lentos e cura Ursos) e Lanterna (flash em área que não apaga no apagão).
- **Inimigos:** Sombra Rastejante, Monstro do Armário (arremessa sapatos), Pesadelo Alado (mergulha nos abajures), Devorador de Luz (come os abajures) e Sombra do Tapete (sai do meio do tapete).
- **Recurso:** Luz, gerada pelos abajures (cada novo custa mais) ou pelo luar que cai do teto.
- **Luz como território:** no escuro os Robôs atiram mais devagar; na luz dos abajures as sombras ficam lentas. As noites vão escurecendo e trazem apagões.
- **Rajadas:** os monstros chegam em grupos e em várias faixas ao mesmo tempo.
- **Campanha:** 5 noites com dificuldade crescente. Cada noite libera brinquedos, monstros e regras novas, e o progresso fica salvo.
- **Tela de preparo:** da Noite 3 em diante você vê os monstros da noite e escolhe quais brinquedos levar na bolsa (4 de 5, 4 de 6, 5 de 6).
- **Obstáculos:** blocos de montar ocupam casas e uma faixa escorregadia acelera os monstros.
- **Estrelas:** até 3 por noite (vencer, nenhum Abajur quebrado, nenhum monstro perto da cama).
- **Última defesa:** um Ratinho de Corda em cada faixa salva a cama uma vez por noite (só nas noites 1–3).
- **Arte:** modelos 3D gerados com a Tripo AI; texturas, ícones e tela de título com Nano Banana (Gemini).

## Como jogar

1. Abra a pasta do projeto no **Unity 6000.3** pelo Unity Hub.
2. Abra a cena pelo menu **Sleep Defenders → Abrir Cena do Jogo**.
3. Aperte **Play**, escolha a noite e clique em **Começar Noite N** (ou **Preparar Noite N**, que abre a tela de preparo).

Controles:

- `1` a `6` ou clique no card: escolhe o brinquedo.
- Clique no tapete: posiciona o brinquedo.
- Clique nos orbes: coleta Luz.
- `R`: guarda (remove) um brinquedo.
- `Esc` ou botão direito: cancela.

O guia completo fica em [`Assets/SleepDefenders/LEIA-ME.md`](Assets/SleepDefenders/LEIA-ME.md). Ele traz arquitetura, prefabs, tags e layers, balanceamento, testes e como gerar a arte de novo.

# Brinquedos vs. Monstros do Escuro — Guia do Projeto

Tower defense em 3D (câmera isométrica/3⁄4) no estilo "plantas contra zumbis".
Os brinquedos, sobre o tapete do quarto, defendem a cama dos monstros das sombras.

- Unity **6000.3** com URP, usando apenas o **Input System novo**.
- Modelos 3D gerados com a **Tripo AI** e importados pelo glTFast.
- Texturas, ícones e arte de título gerados com **Nano Banana** (Gemini).

---

## 1. Primeira execução

1. Abra o projeto no Unity e deixe o Editor em foco até terminar o import.
   - O Package Manager instala o **glTFast** (`com.unity.cloud.gltfast`), que importa os `.glb`.
   - Depois os scripts são compilados.
2. Aparece a janela **"Construir agora os prefabs e a cena do jogo?"**. Clique em *Construir*.
   - Se ela não aparecer, use o menu **Sleep Defenders → Construir Jogo (prefabs + cena)**.
3. O construtor faz tudo de uma vez:
   - cria tags e layers;
   - instala o *TMP Essential Resources*;
   - gera materiais, sprites de UI, prefabs, os `ToyDefinition` e a cena `Assets/SleepDefenders/Scenes/Game.unity`;
   - coloca a cena na Build list.
4. Aperte **Play**, escolha a noite no menu e clique em **Começar Noite N**.

> **Atenção:** *Construir Jogo* recria tudo em `Assets/SleepDefenders/Generated` e a cena `Game.unity`. Alterações manuais nesses arquivos se perdem.
> Para ajustes permanentes, edite os valores no `SleepDefendersBuilder.cs` (seção 6) ou duplique o prefab para fora de `Generated`.
>
> Ao atualizar o projeto para uma versão nova (por exemplo, quando entraram as 5 noites), o Unity pergunta se quer reconstruir. Aceite, ou use o menu *Construir Jogo*.

## 2. Controles

| Ação | Mouse | Teclado |
|---|---|---|
| Escolher brinquedo | clicar no card | `1`, `2`, `3`... (na ordem dos cards) |
| Posicionar | clicar numa célula livre do tapete | — |
| Coletar Luz | clicar no orbe brilhante | — |
| Guardar (remover) brinquedo | botão **Guardar**, depois clicar no brinquedo | `R` |
| Cancelar seleção | botão direito | `Esc` |

A célula sob o mouse fica **verde** (pode colocar), **vermelha** (ocupada) ou **laranja** (modo Guardar).

## 3. Regras e balanceamento

| Brinquedo | Custo | HP | Recarga | Comportamento |
|---|---|---|---|---|
| Abajur | 50 **+25 por abajur no tapete** | 80 | 7,5 s | Gera um orbe de **25 Luz** a cada 10 s (o primeiro sai em 5–8 s) e **ilumina** as casas em volta (3×3) |
| Robô Laser | 100 | 100 | 12 s | Detecta monstros na própria faixa (Raycast ou BoxCast) e atira a cada 1,4 s (20 de dano). **No escuro, a cada 2,8 s** |
| Urso de Pelúcia | 50 | 400 | 25 s | Bloqueia os monstros (inclusive o Devorador de Luz) e vai "amassando" conforme perde HP |
| Carrinho RC | 175 | — | 30 s | Uso único. Ao ser tocado, dispara pela faixa e destrói **todos** os monstros dela |

| Monstro | HP | Velocidade | Dano / intervalo | Especial |
|---|---|---|---|---|
| Sombra Rastejante | 100 | 0,32 (cruza o tapete em ~33 s) | 20 / 1 s | Inimigo padrão, corpo a corpo |
| Monstro do Armário | 450 | 0,2 | 35 / 1,2 s | Tanque; abaixo de 30% de HP fica furioso (+60% de velocidade) |
| Pesadelo Alado | 70 | 0,8 voando, 0,4 no chão | 15 / 0,8 s | Voa por cima da **primeira** defesa e pousa para atacar a segunda |
| Devorador de Luz | 160 | 0,42 | 30 / 0,8 s | Nasce na faixa com **mais abajures**, passa por baixo de Robôs e Carrinhos e só para para **comer Abajures** ou quando um **Urso** bloqueia. A luz não o deixa lento |

### Luz e escuridão

Cada casa do tapete está em um de três estados, mostrados por uma camada sobre o tapete:

| Estado | Visual | Efeito |
|---|---|---|
| Escuro | casa sombreada | Robôs atiram na **metade da velocidade** |
| Luz de cabeceira | normal | Normal. Cada noite define quantas colunas, a partir da cama, ela alcança |
| Luz de abajur | brilho dourado | Robôs normais **e as sombras andam 35% mais devagar** |

- **Apagão:** algumas ondas apagam os abajures por 12 s. Eles continuam gerando Luz, mas não iluminam.
- **Decisão principal:** abajur perto da cama é economia segura; abajur na frente ilumina a zona de combate, mas fica exposto aos monstros e ao Devorador de Luz.

### Ondas em rajada

Cada onda tem `packSize` (monstros em fila na **mesma faixa**) e `lanesAtOnce` (faixas que recebem um grupo ao mesmo tempo). Um Robô sozinho não segura um grupo, então é preciso defender antes de o monstro aparecer.

Outras regras:

- **Luz inicial:** 150 nas noites 1–3 e 200 nas noites 4–5.
- **Luar:** um raio de luar cai do teto a cada 8–12 s e deixa um orbe de 25 Luz numa célula aleatória.
- **Orbes:** somem depois de 12 s se ninguém coletar (piscam nos últimos 3 s).
- **Ratinho de Corda (última defesa), só nas noites 1–3:** cada faixa tem um ratinho de corda na beira da cama. Quando um monstro passa da borda esquerda do tapete, o ratinho dispara e derruba **todos** os monstros da faixa, inclusive os que voam. Funciona **uma vez por noite** em cada faixa.
- **Vitória:** todas as ondas da noite derrotadas. Isso libera a noite seguinte.
- **Derrota:** um monstro alcança a cama.

### Campanha: 5 noites

As decisões começam fáceis e ficam difíceis:

| Noite | Nome | Brinquedos | Novidades | Colunas com luz | Ratinhos | Duração aprox. |
|---|---|---|---|---|---|---|
| 1 | A Primeira Noite | Abajur, Robô Laser | Sombras só nas 3 faixas do meio. Decisão: quantos abajures (cada um custa mais) | 9 (tudo) | sim | ~4 min |
| 2 | Asas no Escuro | + Urso de Pelúcia | Pesadelo Alado; monstros em dupla | 9 | sim | ~6 min |
| 3 | O Armário Rangeu | + Carrinho RC | Monstro do Armário; escuridão no fundo; 2 faixas ao mesmo tempo | 5 | sim | ~6 min |
| 4 | Tempestade de Sombras | todos | Devorador de Luz; um apagão; grupos de 3 | 3 | **não** | ~7,5 min |
| 5 | A Noite Mais Longa | todos | Só a beira da cama iluminada; dois apagões | 1 | **não** | ~8,5 min |

- **Menu:** mostra as 5 noites. As bloqueadas ficam apagadas e as concluídas ganham um selo dourado.
- **Fim da noite:** a vitória mostra **Próxima Noite** e **Menu**; a derrota mostra **Tentar de Novo** e **Menu**.
- **Progresso:** fica salvo em `PlayerPrefs` e continua entre uma sessão e outra.
- **Atalhos no Editor:**
  - *Sleep Defenders → Progresso → Liberar Todas as Noites*;
  - *Sleep Defenders → Progresso → Zerar Progresso*.

**Onde mudar as noites:** os dados ficam em `Generated/Data/Campanha.asset`, e o Inspector mostra cada noite com:

- nome, dica e regras novas (aparecem em "Novidades" no menu);
- Luz inicial e tempo de preparo;
- colunas com luz de cabeceira (`litColumns`) e se há Ratinhos de Corda (`laneGuards`);
- brinquedos liberados;
- ondas, com os monstros, as quantidades, os intervalos, as **faixas permitidas** (`lanes`; vazio = todas), os grupos (`packSize`, `lanesAtOnce`) e o apagão (`blackout`).

Para mudanças permanentes, edite `CreateCampaign()` no `SleepDefendersBuilder.cs`, porque *Construir Jogo* recria esse arquivo.

## 4. Estrutura de pastas

```
Assets/SleepDefenders/
├── Art/
│   ├── Models/     14 modelos .glb da Tripo (personagens, ratinho de corda e cenário)
│   ├── Icons/      ícones PNG transparentes dos cards
│   ├── Textures/   piso, papel de parede e lã do tapete (seamless)
│   └── UI/         title_art.png (tela de título)
├── Scripts/        runtime (assembly "SleepDefenders")
├── Editor/         construtor automático (assembly "SleepDefenders.Editor")
├── Tests/          testes de gameplay em PlayMode
├── Generated/      ⚙ gerado pelo construtor (materiais, prefabs, dados)
└── Scenes/Game.unity   ⚙ gerada pelo construtor
Tools/AssetPipeline/generate_assets.py   pipeline Tripo + Nano Banana
```

## 5. Arquitetura dos scripts

| Script | Função |
|---|---|
| `Core/GridManager.cs` | Grade 5×9 com uma célula `GridCell` (BoxCollider na layer **Grid**) por posição.<br>• `TryGetCellAtScreenPoint` faz o Raycast do mouse e ignora cliques sobre a UI.<br>• Controla a ocupação: `CanPlace`, `TryPlaceToy`, `ReleaseCell`.<br>• Faz o highlight da célula e informa as posições de faixa, borda e cama. |
| `Core/CurrencyManager.cs` | Luz: `AddLight`, `TrySpendLight`, `CanAfford` e o evento `OnLightChanged`. Atualiza o `TMP_Text` e pisca em vermelho quando falta Luz. |
| `Core/GameManager.cs` | Estados Menu, Playing, Won e Lost; vitória, derrota e reinício. Configura a matriz de colisão.<br>• `ConfigureNight(i)` aplica a noite da campanha: ondas, Luz inicial, cards liberados, luar, escuridão e Ratinhos.<br>• Vencer libera a próxima noite; `Restart`, `NextNight` e `BackToMenu` recarregam a cena. |
| `Levels/Campaign.cs` | `Campaign` (ScriptableObject com a lista de noites) e `NightDefinition` (nome, dica, Luz inicial, preparo, colunas com luz, Ratinhos, regras novas, brinquedos e ondas). `Novelties(i)` lista o que aparece pela primeira vez na noite. |
| `Light/LightField.cs` | Luz como território: luz de cabeceira por coluna, luz 3×3 dos abajures e apagões. Responde `IsLit`, `IsLampLit`, `FireIntervalMultiplier` e `EnemySpeedFactorAt`, e desenha a camada escura/dourada sobre o tapete. Só recalcula quando um brinquedo entra ou sai, ou quando começa ou termina um apagão. |
| `Levels/LevelProgress.cs` | Progresso: noites liberadas (PlayerPrefs) e qual noite carregar ao recarregar a cena. |
| `Core/GameLayers.cs` | Constantes de tags e layers, matriz de colisão e a interface `IDamageable`. |
| `Core/ComponentPool.cs` | Object pooling (`PoolRegistry.For(prefab)`) para projéteis e partículas. |
| `Toys/ToyBase.cs` | Base abstrata: `Custo`, `HP`, `Lane`, `TakeDamage`, `Die`/`Remove` e o evento `Destroyed`, que libera a célula. |
| `Toys/ToyLamp.cs` | Abajur: gera orbes de Luz periodicamente. |
| `Toys/LaserRobot.cs` | Robô: varredura a cada 0,1 s com `Physics.Raycast` ou `BoxCast` (campo `detectionMode`) na direção +X, só na própria faixa. Usa projéteis do pool. |
| `Toys/LaserProjectile.cs` | Laser: Rigidbody cinemático, `OnTriggerEnter` com a tag **Enemy** da mesma faixa. |
| `Toys/TeddyBear.cs` | Urso: tanque que bloqueia e "amassa" visualmente. |
| `Toys/RCCar.cs` | Carrinho: `OnTriggerEnter` com **Enemy**, dispara, atropela a faixa e se remove fora da tela. |
| `Toys/LaneGuard.cs` | Ratinho de Corda: última defesa da faixa, fora da grade. Dispara quando um monstro passa da borda do tapete e derruba a faixa inteira, uma vez por noite. |
| `Toys/ToyDefinition.cs` | ScriptableObject do card: prefab, ícone, descrição, recarga e `extraCostPerCopy` (preço crescente; `CurrentCost` conta os iguais no tapete). |
| `Enemies/EnemyBase.cs` | Movimento linear para a cama (−X).<br>• Ao encostar num brinquedo da mesma faixa (trigger com tag **Defender**), para e ataca em intervalos.<br>• Se cruzar a linha da cama, causa a derrota. |
| `Enemies/ShadowCreeper.cs`, `ClosetMonster.cs`, `WingedNightmare.cs`, `LightEater.cs` | Variações dos monstros (fúria do tanque, voo que pula a primeira defesa, Devorador que caça abajures). |
| `Enemies/EnemySpawner.cs` | Ondas em faixas aleatórias, só entre as **faixas permitidas** da onda (`lanes`) e sem repetir a faixa seguida, com embaralhamento Fisher–Yates.<br>• Rajadas: `packSize` monstros em fila por faixa, em `lanesAtOnce` faixas ao mesmo tempo.<br>• Monstros podem escolher a faixa (`EnemyBase.PreferredLane`).<br>• O evento `WaveStarted` faz o HUD mostrar o aviso de cada onda e o `LightField` disparar os apagões. |
| `Light/LightOrb.cs`, `MoonlightSpawner.cs` | Orbes coletáveis (layer **Pickup**) e o luar que cai do teto. |
| `Gameplay/ToyPlacementController.cs` | Entrada do jogador (Input System): cards, posicionar, coletar, guardar e atalhos. `SetAvailableToys` mostra só os cards liberados na noite e renumera os atalhos. |
| `UI/*` | Cards com recarga, botão Guardar, cards das noites (`NightButtonUI`) e HUD (menu de noites, ondas, barra de progresso, avisos e telas de fim). |
| `FX/*` | Animação procedural (respiração, passos, voo, ataque, *squash*), flash de dano, partículas e efeitos sonoros sintetizados. |

As personagens não têm rig: toda a animação é **procedural**, no `ProceduralAnimator`, aplicada ao filho `Visual`. Assim, qualquer modelo novo da Tripo funciona sem precisar de Animator.

## 6. Tags, Layers e física

**Tags:** `Defender`, `Enemy`, `Projectile`, `LightOrb`
**Layers:** `Grid`, `Enemy`, `Defender`, `Projectile`, `Pickup`

O construtor cria tudo isso sozinho. A matriz de colisão é aplicada em tempo de execução por `GameLayers.ConfigureCollisionMatrix()` e desliga estes pares:

- Inimigo × inimigo, inimigo × Grid e inimigo × Pickup.
- Projétil × projétil, Defender, Grid e Pickup.
- Defender × Defender, Grid e Pickup.

### Configuração dos prefabs

| Prefab | Collider | Rigidbody | Tag / Layer | Observações |
|---|---|---|---|---|
| Toy_Abajur | BoxCollider (físico) | — | Defender / Defender | Point Light "Glow" pulsando |
| Toy_RoboLaser | BoxCollider (**Trigger**) | — | Defender / Defender | Filho `Muzzle` = ponta do laser |
| Toy_Urso | BoxCollider (físico) | — | Defender / Defender | HP 400 |
| Enemy_* (4 monstros) | CapsuleCollider (**Trigger**) | **Cinemático**, interpolado | Enemy / Enemy | Movido com `MovePosition` |
| LaserProjectile | SphereCollider (**Trigger**) | **Cinemático** | Projectile / Projectile | Com TrailRenderer |
| Toy_CarrinhoRC | BoxCollider (**Trigger**) | Rigidbody (cinemático) | Defender / Defender | Ativado por `OnTriggerEnter` com tag **Enemy** |
| LightOrb | SphereCollider (Trigger) | — | LightOrb / Pickup | Clicável |
| LaneGuard_RatinhoDeCorda | — | — | — | Não usa física: compara posições com a lista de monstros vivos. Um por faixa, na beira da cama. |

Por que o colisor dos monstros é **Trigger**? Monstros e brinquedos não podem se empurrar fisicamente. Com Rigidbody cinemático + trigger, o Unity dispara `OnTriggerEnter`/`OnTriggerStay` mesmo contra os colliders estáticos dos brinquedos, seja o box físico do Urso ou o trigger do Robô. Aí o próprio `EnemyBase` decide parar e atacar.

Todas as consultas de física usam `QueryTriggerInteraction.Collide` para enxergar triggers.

### Montando um prefab de brinquedo à mão

1. Crie um GameObject vazio na tag e layer **Defender**.
2. Adicione o script (subclasse de `ToyBase`) e o collider da tabela acima.
3. Crie um filho **`Visual`** e, dentro dele, o modelo.
   - Os modelos da Tripo olham para **−X**. Gire o `Visual` em Y ≈ 205° para o brinquedo olhar para a direita, onde estão os monstros.
4. Crie um **ToyDefinition** em *Create → Sleep Defenders → Toy Definition*, aponte o prefab e o ícone, e coloque-o num card do HUD (`ToyCardUI.definition`).

## 7. Regerar arte (Tripo + Nano Banana)

As chaves vêm **somente** das variáveis de ambiente e nunca são impressas:

```powershell
$env:TRIPO_API_KEY  = "..."   # Tripo AI
$env:GEMINI_API_KEY = "..."   # Nano Banana (Gemini)
```

```bash
python Tools/AssetPipeline/generate_assets.py all
```

Etapas disponíveis:

- `concepts`: arte conceitual de cada personagem ou prop, com Nano Banana. Gera também os ícones dos cards.
- `textures`: piso, papel de parede, tapete e arte de título.
- `models`: envia o conceito para a Tripo (image-to-model) e baixa o `.glb` em `Art/Models`.
- `all`: as três em sequência.

Opções:

- `--only teddy_bear,rc_car` processa só esses ids.
- `--force` gera de novo mesmo que o arquivo já exista.

As tarefas da Tripo ficam salvas em `Tools/AssetPipeline/output/state.json`. Se a execução cair, rodar de novo continua de onde parou. Cada modelo custa ~30 créditos.

Depois de trocar modelos, rode **Sleep Defenders → Construir Jogo** de novo. A escala, o "pé no chão" e a direção são ajustados automaticamente. Modelos ausentes viram placeholders e são listados no aviso final.

## 8. Testes

*Window → General → Test Runner → PlayMode → Run All*. Rode o *Construir Jogo* antes.

Os 21 testes guardam e restauram o seu progresso das noites. Um deles joga a Noite 5 inteira contra uma defesa forte (~1 min). Eles cobrem:

- Raycast do mouse na grade.
- Custo, ocupação e recarga.
- Robô matando só na própria faixa.
- Urso bloqueando o Monstro do Armário.
- Pesadelo Alado pulando a primeira defesa.
- Carrinho limpando a faixa e liberando a célula.
- Abajur gerando Luz.
- Derrota ao chegar na cama.
- Ratinho de Corda salvando a faixa uma única vez.
- Vitória numa noite curta.
- Campanha com 5 noites, cada uma mais longa que a anterior.
- Noite 1 com só 2 cards e monstros só nas faixas do meio.
- Vencer uma noite libera a próxima, que começa direto.
- Noites bloqueadas desabilitadas no menu.
- Preço do Abajur subindo e descendo com os abajures no tapete.
- Ondas chegando em grupos, em faixas diferentes.
- Escuridão deixando o Robô mais lento e a luz do abajur deixando as sombras mais lentas.
- Apagão disparado pela onda e as luzes voltando.
- Devorador de Luz passando pelo Robô, comendo o Abajur e parando no Urso.
- Noites mais escuras, sem Ratinhos nas noites 4–5 e com apagões.
- Noite 5 vencível com uma defesa forte e bem iluminada.

## 9. Problemas comuns

- **Modelos cor-de-rosa / magenta.** O shader do glTFast não compilou. Na janela Project, abra *Packages → Unity glTFast → Runtime → Shader*, clique com o botão direito e use *Reimport*. Se o projeto estiver num caminho muito longo, o Windows (MAX_PATH) pode impedir a compilação: mova o projeto para um caminho curto.
- **Texto da UI invisível.** Falta o *TMP Essential Resources*. Rode *Construir Jogo* de novo, porque ele instala automaticamente.
- **Cliques não funcionam.** Confira se a cena tem um `EventSystem` com **InputSystemUIInputModule**. O projeto usa só o Input System novo.
- **O Unity abriu mas nenhuma janela aparece, ou o Hub não abre o projeto.** Um Editor anterior pode ter ficado preso em segundo plano segurando o projeto. Finalize o `Unity.exe` no Gerenciador de Tarefas e abra de novo pelo Hub.

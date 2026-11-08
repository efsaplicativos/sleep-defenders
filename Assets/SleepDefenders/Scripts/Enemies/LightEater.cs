using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Devorador de Luz — caçador de abajures.
    ///   - Nasce na faixa com MAIS abajures (entre as permitidas pela onda).
    ///   - Se esgueira por baixo de Robôs e Carrinhos sem parar: só para para comer Abajures
    ///     (que somem rápido) ou quando um Urso de Pelúcia bloqueia o caminho.
    ///   - A luz do abajur não o deixa mais lento.
    /// Valores no prefab: HP 160, velocidade 0,42, dano 30 a cada 0,8 s.
    /// </summary>
    public class LightEater : EnemyBase
    {
        [Header("Devorador")]
        [SerializeField] private Color gulpColor = new Color(1f, 0.85f, 0.35f, 1f);

        protected override bool SlowedByLight => false;

        protected override bool ShouldEngage(ToyBase toy) => toy is ToyLamp || toy is TeddyBear;

        protected override void OnStartAttack(ToyBase toy)
        {
            if (toy is ToyLamp)
                Vfx.Spawn(VfxType.Sparkle, toy.transform.position + Vector3.up * 0.6f, gulpColor, 0.9f);
        }

        /// <summary>Procura a faixa com mais abajures; empate = sorteio entre as melhores. Sem abajures = sem preferência.</summary>
        public override int PreferredLane(IReadOnlyList<int> options)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || options == null || options.Count == 0) return -1;

            int best = 0, chosen = -1, ties = 0;
            foreach (int lane in options)
            {
                int lamps = 0;
                for (int c = 0; c < grid.Columns; c++)
                    if (grid.GetOccupant(lane, c) is ToyLamp lamp && lamp.IsAlive) lamps++;
                if (lamps == 0 || lamps < best) continue;
                if (lamps > best)
                {
                    best = lamps;
                    chosen = lane;
                    ties = 1;
                }
                else if (Random.Range(0, ++ties) == 0) chosen = lane; // sorteio justo entre empatadas
            }
            return chosen;
        }
    }
}

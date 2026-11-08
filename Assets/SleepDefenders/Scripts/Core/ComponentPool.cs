using System.Collections.Generic;
using UnityEngine;

namespace SleepDefenders
{
    /// <summary>
    /// Pool genérico e simples de componentes. Evita Instantiate/Destroy a cada tiro de laser,
    /// o que reduz alocações e picos do Garbage Collector.
    /// </summary>
    public class ComponentPool<T> where T : Component
    {
        private readonly T prefab;
        private readonly Transform container;
        private readonly Stack<T> available = new Stack<T>();

        public ComponentPool(T prefab, Transform container, int prewarm = 0)
        {
            this.prefab = prefab;
            this.container = container;
            for (int i = 0; i < prewarm; i++)
            {
                T item = Object.Instantiate(prefab, container);
                item.gameObject.SetActive(false);
                available.Push(item);
            }
        }

        public T Get(Vector3 position, Quaternion rotation)
        {
            T item = null;
            // Itens podem ter sido destruídos junto com a cena; descarta referências mortas.
            while (available.Count > 0 && item == null)
                item = available.Pop();

            if (item == null)
                item = Object.Instantiate(prefab, position, rotation, container);
            else
                item.transform.SetPositionAndRotation(position, rotation);

            item.gameObject.SetActive(true);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            item.gameObject.SetActive(false);
            available.Push(item);
        }
    }

    /// <summary>
    /// Registro global de pools por prefab, para que qualquer script peça um objeto
    /// sem precisar conhecer quem criou o pool.
    /// </summary>
    public static class PoolRegistry
    {
        private static readonly Dictionary<Object, object> pools = new Dictionary<Object, object>();
        private static Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pools.Clear();
            root = null;
        }

        public static ComponentPool<T> For<T>(T prefab, int prewarm = 0) where T : Component
        {
            if (root == null)
            {
                pools.Clear(); // a cena foi recarregada: os pools antigos apontam para objetos destruídos
                root = new GameObject("[Pools]").transform;
            }

            if (!pools.TryGetValue(prefab, out object pool))
            {
                var container = new GameObject($"Pool_{prefab.name}").transform;
                container.SetParent(root, false);
                pool = new ComponentPool<T>(prefab, container, prewarm);
                pools[prefab] = pool;
            }
            return (ComponentPool<T>)pool;
        }
    }
}

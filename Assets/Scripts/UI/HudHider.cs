namespace SIRMED.UI
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// HudHider — oculta elementos del HUD mientras un panel está abierto y los
    /// restaura al cerrarlo, compartido entre paneles (HotspotUIPanel, TriviaPanel,
    /// NpcDialoguePanel, DirectorAdvicePanel).
    ///
    /// Lleva un conteo por elemento: el estado original se guarda con el primer
    /// panel que lo oculta y solo se restaura cuando el último panel que lo ocultó
    /// se cierra. Así, si el banner del Director aparece encima de una trivia, el
    /// HUD no queda escondido ni reaparece antes de tiempo, sin importar el orden
    /// en que se cierren.
    /// </summary>
    public static class HudHider
    {
        private static readonly Dictionary<object, GameObject[]> _owners = new Dictionary<object, GameObject[]>();
        private static readonly Dictionary<GameObject, int> _hideCount = new Dictionary<GameObject, int>();
        private static readonly Dictionary<GameObject, bool> _wasActive = new Dictionary<GameObject, bool>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _owners.Clear();
            _hideCount.Clear();
            _wasActive.Clear();
        }

        /// <summary>Oculta <paramref name="elements"/> en nombre de <paramref name="owner"/>. Llamar dos veces sin Restore no hace nada.</summary>
        public static void Hide(object owner, GameObject[] elements)
        {
            if (owner == null || elements == null || elements.Length == 0) return;
            if (_owners.ContainsKey(owner)) return;

            _owners[owner] = (GameObject[])elements.Clone();
            foreach (GameObject go in elements)
            {
                if (go == null) continue;

                _hideCount.TryGetValue(go, out int count);
                if (count == 0) _wasActive[go] = go.activeSelf;
                _hideCount[go] = count + 1;
                go.SetActive(false);
            }
        }

        /// <summary>Libera lo que <paramref name="owner"/> ocultó; cada elemento vuelve a su estado original cuando ya nadie lo oculta.</summary>
        public static void Restore(object owner)
        {
            if (owner == null || !_owners.TryGetValue(owner, out GameObject[] elements)) return;
            _owners.Remove(owner);

            foreach (GameObject go in elements)
            {
                if (ReferenceEquals(go, null) || !_hideCount.TryGetValue(go, out int count)) continue;

                if (count > 1)
                {
                    _hideCount[go] = count - 1;
                    continue;
                }

                _hideCount.Remove(go);
                if (_wasActive.TryGetValue(go, out bool wasActive))
                {
                    _wasActive.Remove(go);
                    if (go != null) go.SetActive(wasActive);
                }
            }
        }
    }
}

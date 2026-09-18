namespace SIRMED.Gameplay.Environment
{
    using System.Collections;
    using System.Collections.Generic;
    using Unity.Mathematics;
    using UnityEngine;
    using UnityEngine.Splines;

    /// <summary>
    /// EvacuationRouteController — cinta plana geo-anclada que marca la ruta de
    /// evacuación (GDD; roadmap de MIGRATION_CONTEXT.md §7.2 fase 3 / §11).
    ///
    /// Reemplaza el enfoque del proyecto original (LineRenderer + curva
    /// Catmull-Rom escrita a mano + SetTextureOffset en Update, ver
    /// GPS_AR_Test_game/Assets/_Shared/Scripts/Environment/EvacuationRouteController.cs)
    /// por una malla real (una cinta plana, no una línea delgada):
    ///
    ///   • Los waypoints son GameObjects con RouteWaypoint + su propio
    ///     ARGeospatialCreatorAnchor, igual que un hotspot — no existe un
    ///     espacio de Editor fijo en runtime, cada punto solo es confiable una
    ///     vez que su anchor resuelve (ver MIGRATION_CONTEXT.md §3 y §11).
    ///   • Una vez todos los waypoints están anclados, se construye un
    ///     UnityEngine.Splines.Spline con esas posiciones de mundo
    ///     (TangentMode.AutoSmooth reemplaza el Catmull-Rom manual del original).
    ///   • La malla de la cinta se genera muestreando ese spline a mano (no con
    ///     SplineExtrude): en com.unity.splines 2.9.1 la forma de tubo por
    ///     defecto de SplineExtrude fuerza Sides >= 3 (ver Sides setter en
    ///     SplineExtrude.cs) y el corte transversal personalizado
    ///     (IExtrudeShape) es 'internal' al paquete, así que no hay forma de
    ///     pedirle desde este código un corte plano de 2 lados. Un builder
    ///     propio da control total sobre ancho, flotado sobre el suelo y el
    ///     UV.y en metros que necesita el shader para tilear la flecha.
    ///   • El material (shader "SIRMED/EvacuationRibbon") anima el flujo de
    ///     flechas en el propio shader vía _Time, sin tocar el material desde
    ///     C# cada frame — solo el fade in/out toca _Color (alpha) al mostrar
    ///     u ocultar la ruta.
    ///
    /// API pública: Show()/Hide(), igual firma que el original — se invoca
    /// desde HotspotController.ClosePanel() cuando data.activatesEvacuationRoute
    /// es true.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class EvacuationRouteController : MonoBehaviour
    {
        public static EvacuationRouteController Instance { get; private set; }

        [Header("Ruta — Waypoints")]
        [Tooltip("GameObjects con RouteWaypoint + ARGeospatialCreatorAnchor, en orden desde el origen hasta el Punto de Encuentro.")]
        public RouteWaypoint[] waypoints;

        [Tooltip("Muestras de la cinta por metro de longitud del spline. Más = curva más suave, más triángulos.")]
        [Range(0.2f, 5f)]
        public float samplesPerMeter = 1f;

        [Header("Punto de Encuentro")]
        [Tooltip("Al acercarse a este Transform la cinta se oculta automáticamente. Normalmente el mismo GameObject del hotspot 'PUNTO DE ENCUENTRO'.")]
        public Transform puntoDeEncuentro;

        [Tooltip("Radio (m) para el auto-ocultamiento.")]
        public float hideRadius = 4f;

        [Header("Apariencia")]
        [Tooltip("Ancho de la cinta en metros.")]
        public float ribbonWidth = 0.5f;

        [Tooltip("Altura (m) sobre cada waypoint para que la cinta 'flote' un poco sobre el piso real en vez de enterrarse en terreno irregular.")]
        public float heightOffset = 0.15f;

        [Tooltip("Material con shader 'SIRMED/EvacuationRibbon' (o compatible, con propiedad _Color). Se instancia en runtime, no se modifica el asset compartido.")]
        public Material ribbonMaterial;

        [Tooltip("Color/alpha máximo de la cinta una vez visible. El shader multiplica esto por su propia textura de flechas.")]
        public Color tint = new Color(0.15f, 0.95f, 0.35f, 0.85f);

        [Header("Fade")]
        public float fadeInDuration = 0.8f;
        public float fadeOutDuration = 0.5f;

        // ── Internos ──────────────────────────────────────────────────────────────
        private SplineContainer _container;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _materialInstance;
        private Coroutine _fadeRoutine;
        private float _currentAlpha;
        private bool _isVisible;
        private bool _built;

        // ── Lifecycle ─────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _container = GetComponent<SplineContainer>();
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "EvacuationRibbon" };
            _meshFilter.sharedMesh = _mesh;

            if (ribbonMaterial != null)
                _meshRenderer.material = ribbonMaterial; // instancia propia, no toca el asset compartido
            _materialInstance = _meshRenderer.material;

            ApplyAlpha(0f);
            _meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Longitud mínima (m) para aceptar la spline como válida. Justo después de que
        // ARCore reparenta un waypoint bajo su ARGeospatialAnchor recién resuelto, el
        // Transform de ese anchor puede tardar uno o más frames en recibir su pose real
        // trackeada — durante esa ventana wp.transform.position lee (0,0,0) para varios
        // waypoints a la vez, colapsando la spline a un punto. AllWaypointsReady() ya
        // pasó (todos reparentados), pero las posiciones aún no son de fiar, así que se
        // valida la longitud resultante antes de aceptar el build como definitivo.
        private const float _minValidSplineLength = 1f;

        private void Update()
        {
            if (!_built)
            {
                if (AllWaypointsReady())
                {
                    BuildSpline();
                    float length = _container.Spline.GetLength();
                    if (length < _minValidSplineLength)
                        return; // spline aún degenerada: no se marca _built, se reintenta el siguiente frame

                    BuildRibbonMesh();
                    _built = true;
                }
                return;
            }

            if (!_isVisible || puntoDeEncuentro == null || Camera.main == null) return;

            Vector3 flat = puntoDeEncuentro.position - Camera.main.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude <= hideRadius * hideRadius)
                Hide();
        }

        // ── API pública ───────────────────────────────────────────────────────────
        public void Show()
        {
            if (!_built || _isVisible) return;
            _isVisible = true;
            _meshRenderer.enabled = true;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 1f, fadeInDuration));
        }

        public void Hide()
        {
            if (!_isVisible) return;
            _isVisible = false;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(_currentAlpha, 0f, fadeOutDuration));
        }

        // ── Anclaje geoespacial ───────────────────────────────────────────────────
        private bool AllWaypointsReady()
        {
            if (waypoints == null || waypoints.Length < 2) return false;
            foreach (var wp in waypoints)
                if (wp == null || !wp.IsAnchorReady()) return false;
            return true;
        }

        // ── Spline: curva suave a través de los waypoints ya anclados ────────────
        private void BuildSpline()
        {
            var positions = new List<float3>(waypoints.Length);
            foreach (var wp in waypoints)
            {
                Vector3 local = transform.InverseTransformPoint(wp.transform.position);
                positions.Add(new float3(local.x, local.y, local.z));
            }

            _container.Spline = new Spline(positions, TangentMode.AutoSmooth);
        }

        // ── Malla de la cinta ─────────────────────────────────────────────────────
        private void BuildRibbonMesh()
        {
            Spline spline = _container.Spline;
            float length = spline.GetLength();
            int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length * samplesPerMeter) + 1);

            var vertices = new List<Vector3>(sampleCount * 2);
            var uvs = new List<Vector2>(sampleCount * 2);
            var triangles = new List<int>((sampleCount - 1) * 6);

            float half = ribbonWidth * 0.5f;
            float distance = 0f;
            Vector3 prevPos = Vector3.zero;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                spline.Evaluate(t, out float3 posLocal, out float3 tanLocal, out _);

                Vector3 pos = new Vector3(posLocal.x, posLocal.y, posLocal.z) + Vector3.up * heightOffset;
                Vector3 tangent = new Vector3(tanLocal.x, tanLocal.y, tanLocal.z).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

                if (i > 0) distance += Vector3.Distance(pos, prevPos);
                prevPos = pos;

                vertices.Add(pos - right * half);
                vertices.Add(pos + right * half);
                uvs.Add(new Vector2(0f, distance));
                uvs.Add(new Vector2(1f, distance));

                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    int b = a + 1;
                    int c = i * 2;
                    int d = c + 1;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
        }

        // ── Fade ──────────────────────────────────────────────────────────────────
        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ApplyAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            ApplyAlpha(to);

            if (to <= 0f) _meshRenderer.enabled = false;
        }

        private void ApplyAlpha(float alpha)
        {
            _currentAlpha = alpha;
            if (_materialInstance == null) return;

            Color c = tint;
            c.a = tint.a * alpha;
            _materialInstance.SetColor("_Color", c);
        }
    }
}

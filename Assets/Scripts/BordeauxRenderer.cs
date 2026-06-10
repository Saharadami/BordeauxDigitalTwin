using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class BordeauxRenderer : MonoBehaviour
{
    [Header("Refs")]
    public BordeauxDataManager roadDataManager;

    [Header("Render")]
    public bool renderOnStart = false;
    public int maxEdgesToDraw = -1;       // -1 = همه
    public bool drawNodes = false;    // پیش‌فرض خاموش - نودها شلوغ میکنن

    [Header("Sizes")]
    [Tooltip("0 = از نوع جاده بگیر  |  بزرگتر از 0 = override همه")]
    public float lineWidthOverride = 0f;
    public float nodeSize = 8f;

    [Header("Materials")]
    public Material lineMaterial;
    public Material nodeMaterial;

    [Header("Filters")]
    public bool drawCat1 = true;
    public bool drawCat2 = true;
    public bool drawCat3 = true;
    public bool drawCat4 = true;
    public bool drawCat5 = true;
    public bool drawGrandTrafic = true;

    [Header("Camera Focus")]
    public Camera targetCamera;
    public float cameraHeight = 600f;
    [Range(30f, 90f)]
    public float cameraTilt = 55f;

    // ── Private ───────────────────────────────────────────────────────────────

    private GameObject renderRoot;
    private readonly List<GameObject> spawned = new List<GameObject>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (renderOnStart) RenderGraph();
    }

    // ── Public ────────────────────────────────────────────────────────────────

    [ContextMenu("Render Graph")]
    public void RenderGraph()
    {
        ClearGraph();

        if (roadDataManager == null)
            roadDataManager = FindObjectOfType<BordeauxDataManager>();

        if (roadDataManager == null)
        {
            Debug.LogError("[RoadRenderer] BordeauxDataManager not found.");
            return;
        }

        if (roadDataManager.edges == null || roadDataManager.edges.Count == 0)
        {
            Debug.LogWarning("[RoadRenderer] No edges — run Load Roads first.");
            return;
        }

        renderRoot = new GameObject("RoadGraphRenderRoot");
        renderRoot.transform.SetParent(transform, false);

        int drawn = 0;

        foreach (BxEdge edge in roadDataManager.edges)
        {
            if (!ShouldDraw(edge)) continue;
            if (maxEdgesToDraw >= 0 && drawn >= maxEdgesToDraw) break;

            CreateEdge(edge);
            drawn++;
        }

        if (drawNodes) CreateNodes();

        FocusCamera();

        Debug.Log($"[RoadRenderer] Rendered {drawn} edges  {(drawNodes ? roadDataManager.nodes.Count : 0)} nodes.");
    }

    [ContextMenu("Clear Graph")]
    public void ClearGraph()
    {
        foreach (GameObject go in spawned)
            if (go != null) Kill(go);

        spawned.Clear();

        if (renderRoot != null) { Kill(renderRoot); renderRoot = null; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void CreateEdge(BxEdge edge)
    {
        GameObject go = new GameObject($"Edge_{edge.id}");
        go.transform.SetParent(renderRoot.transform, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();

        lr.useWorldSpace = true;
        lr.positionCount = edge.points.Count;
        lr.SetPositions(edge.points.ToArray());

        float w = lineWidthOverride > 0f ? lineWidthOverride : edge.width;
        lr.startWidth = w;
        lr.endWidth = w;

        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;

        Material mat = lineMaterial != null ? lineMaterial : MakeUnlitMat(edge.color);
        lr.material = mat;
        lr.startColor = edge.color;
        lr.endColor = edge.color;

        spawned.Add(go);
    }

    private void CreateNodes()
    {
        Material mat = nodeMaterial != null ? nodeMaterial : MakeUnlitMat(Color.white);

        foreach (BxNode node in roadDataManager.nodes)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = $"Node_{node.id}";
            s.transform.SetParent(renderRoot.transform, false);
            s.transform.position = node.position;
            s.transform.localScale = Vector3.one * nodeSize;

            Collider col = s.GetComponent<Collider>();
            if (col != null) Kill(col);

            Renderer r = s.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;

            spawned.Add(s);
        }
    }

    private void FocusCamera()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || roadDataManager.nodes == null || roadDataManager.nodes.Count == 0) return;

        Vector3 min = roadDataManager.nodes[0].position;
        Vector3 max = roadDataManager.nodes[0].position;

        foreach (BxNode n in roadDataManager.nodes)
        {
            min = Vector3.Min(min, n.position);
            max = Vector3.Max(max, n.position);
        }

        Vector3 center = (min + max) * 0.5f;
        float fwdOffset = cameraHeight / Mathf.Tan(cameraTilt * Mathf.Deg2Rad);

        cam.transform.position = new Vector3(center.x, cameraHeight, center.z - fwdOffset);
        cam.transform.LookAt(center);

        Debug.Log($"[RoadRenderer] Camera → center {center}  height {cameraHeight}");
    }

    private bool ShouldDraw(BxEdge edge)
    {
        if (edge.grandTrafic) return drawGrandTrafic;

        switch (edge.catDig)
        {
            case 1: return drawCat1;
            case 2: return drawCat2;
            case 3: return drawCat3;
            case 4: return drawCat4;
            case 5: return drawCat5;
            default: return true;
        }
    }

    private Material MakeUnlitMat(Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Sprites/Default");

        Material mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    private void Kill(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
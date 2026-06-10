using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RoadRenderer : MonoBehaviour
{
    [Header("Refs")]
    public RoadDataManager roadDataManager;

    [Header("Render")]
    public bool renderOnStart = false;
    [Tooltip("All Edges — set -1 for unlimited")]
    public int maxEdgesToDraw = -1;
    public bool drawNodes = true;

    [Header("Sizes")]
    [Tooltip("اگه 0 باشه، ضخامت از نوع جاده گرفته میشه")]
    public float lineWidthOverride = 0f;
    public float nodeSize = 10f;

    [Header("Materials")]
    public Material lineMaterial;
    public Material nodeMaterial;

    [Header("Filters")]
    public bool drawMotorwayAndTrunk = true;
    public bool drawPrimarySecondary = true;
    public bool drawResidentialAndOther = true;

    [Header("Camera Focus")]
    [Tooltip("خالی بذار تا Camera.main استفاده بشه")]
    public Camera targetCamera;
    public float cameraHeight = 800f;
    [Range(30f, 90f)]
    public float cameraTilt = 60f;

    private GameObject renderRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        if (renderOnStart)
            RenderGraph();
    }

    [ContextMenu("Render Graph")]
    public void RenderGraph()
    {
        ClearGraph();

        if (roadDataManager == null)
        {
            roadDataManager = FindObjectOfType<RoadDataManager>();
            if (roadDataManager == null)
            {
                Debug.LogError("RoadDataManager not found.");
                return;
            }
        }

        if (roadDataManager.edges == null || roadDataManager.edges.Count == 0)
        {
            Debug.LogWarning("No edges found. Click Load Roads first.");
            return;
        }

        renderRoot = new GameObject("RoadGraphRenderRoot");
        renderRoot.transform.SetParent(transform, false);

        int drawnEdges = 0;

        foreach (var edge in roadDataManager.edges)
        {
            if (!ShouldDraw(edge.highway)) continue;
            if (maxEdgesToDraw >= 0 && drawnEdges >= maxEdgesToDraw) break;

            CreateEdgeObject(edge);
            drawnEdges++;
        }

        if (drawNodes)
            CreateNodeObjects();

        FocusCameraOnGraph();

        Debug.Log($"Rendered {drawnEdges} edges and {(drawNodes ? roadDataManager.nodes.Count : 0)} nodes.");
    }

    [ContextMenu("Clear Graph")]
    public void ClearGraph()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
            if (spawnedObjects[i] != null)
                DestroySafe(spawnedObjects[i]);

        spawnedObjects.Clear();

        if (renderRoot != null)
        {
            DestroySafe(renderRoot);
            renderRoot = null;
        }
    }

    private void CreateEdgeObject(RoadEdge edge)
    {
        GameObject go = new GameObject($"Edge_{edge.id}");
        go.transform.SetParent(renderRoot.transform, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();

        lr.useWorldSpace = true;
        lr.positionCount = edge.points.Count;
        lr.SetPositions(edge.points.ToArray());

        // ضخامت: اگه override تنظیم شده باشه اون رو بگیر، وگرنه از نوع جاده
        float w = 30f;
        lr.startWidth = w;
        lr.endWidth = w;

        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;

        lr.material = lineMaterial != null ? lineMaterial : CreateDefaultUnlitMaterial();
        lr.startColor = edge.color;
        lr.endColor = edge.color;

        spawnedObjects.Add(go);
    }

    private void CreateNodeObjects()
    {
        Material mat = nodeMaterial != null ? nodeMaterial : CreateDefaultUnlitMaterial(Color.white);

        foreach (var node in roadDataManager.nodes)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Node_{node.id}";
            sphere.transform.SetParent(renderRoot.transform, false);
            sphere.transform.position = node.position;
            sphere.transform.localScale = Vector3.one * nodeSize;

            Collider col = sphere.GetComponent<Collider>();
            if (col != null) DestroySafe(col);

            Renderer r = sphere.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;

            spawnedObjects.Add(sphere);
        }
    }

    private void FocusCameraOnGraph()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || roadDataManager.nodes == null || roadDataManager.nodes.Count == 0)
            return;

        Vector3 min = roadDataManager.nodes[0].position;
        Vector3 max = roadDataManager.nodes[0].position;

        foreach (var node in roadDataManager.nodes)
        {
            min = Vector3.Min(min, node.position);
            max = Vector3.Max(max, node.position);
        }

        Vector3 center = (min + max) * 0.5f;
        float tiltRad = cameraTilt * Mathf.Deg2Rad;
        float fwdOffset = cameraHeight / Mathf.Tan(tiltRad);

        cam.transform.position = new Vector3(center.x, cameraHeight, center.z - fwdOffset);
        cam.transform.LookAt(center);

        Debug.Log($"Camera focused → centre {center}  height {cameraHeight}");
    }

    private bool ShouldDraw(string highway)
    {
        string h = (highway ?? "").ToLowerInvariant();
        if (h.Contains("motorway") || h.Contains("trunk")) return drawMotorwayAndTrunk;
        if (h.Contains("primary") || h.Contains("secondary")) return drawPrimarySecondary;
        return drawResidentialAndOther;
    }

    private Material CreateDefaultUnlitMaterial(Color? color = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        Color c = color ?? Color.white;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        return mat;
    }

    private void DestroySafe(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
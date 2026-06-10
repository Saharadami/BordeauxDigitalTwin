using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Data classes
// ─────────────────────────────────────────────────────────────────────────────

[Serializable]
public class BxNode
{
    public int id;
    public Vector3 position;
    public List<int> connectedEdgeIds = new List<int>();
}

[Serializable]
public class BxEdge
{
    public int id;
    public int startNodeId;
    public int endNodeId;
    public string nomVoie;        // nom_voie
    public int catDig;         // cat_dig  1-5
    public int groupe;         // groupe
    public bool grandTrafic;    // voiegrandtrafic
    public float lengthMeters;
    public Color color;
    public float width;
    public List<Vector3> points = new List<Vector3>();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Manager
// ─────────────────────────────────────────────────────────────────────────────

public class BordeauxDataManager : MonoBehaviour
{
    [Header("Input")]
    public string geoJsonFileName = "Bordeaux.geojson";
    public CesiumGeoreference georeference;

    [Header("Settings")]
    public bool loadOnStart = false;
    public float nodeMergeToleranceMeters = 1f;

    [Header("Output")]
    public List<BxNode> nodes = new List<BxNode>();
    public List<BxEdge> edges = new List<BxEdge>();

    private readonly Dictionary<string, int> nodeLookup = new Dictionary<string, int>();

    // ── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (loadOnStart) LoadRoads();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    [ContextMenu("Load Roads")]
    public void LoadRoads()
    {
        nodes.Clear();
        edges.Clear();
        nodeLookup.Clear();

        // Georeference
        if (georeference == null)
            georeference = FindObjectOfType<CesiumGeoreference>();

        if (georeference == null)
        {
            Debug.LogError("[RoadDataManager] CesiumGeoreference not found.");
            return;
        }

        // File
        string path = Path.Combine(Application.streamingAssetsPath, geoJsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"[RoadDataManager] File not found: {path}");
            return;
        }

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception e) { Debug.LogError($"[RoadDataManager] Read error: {e.Message}"); return; }

        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception e) { Debug.LogError($"[RoadDataManager] Parse error: {e.Message}"); return; }

        JArray features = root["features"] as JArray;
        if (features == null)
        {
            Debug.LogError("[RoadDataManager] No 'features' array.");
            return;
        }

        int skipped = 0;

        foreach (JToken token in features)
        {
            JObject feature = token as JObject;
            if (feature == null) continue;

            JObject geometry = feature["geometry"] as JObject;
            JObject properties = feature["properties"] as JObject;
            if (geometry == null || properties == null) continue;

            if (geometry["type"]?.ToString() != "LineString") continue;

            JArray coordinates = geometry["coordinates"] as JArray;
            if (coordinates == null || coordinates.Count < 2) continue;

            // ── Read properties ───────────────────────────────────────────
            string nomVoie = properties["nom_voie"]?.ToString() ?? "unknown";
            int catDig = ParseInt(properties["cat_dig"]?.ToString(), 3);
            int groupe = ParseInt(properties["groupe"]?.ToString(), 1);
            bool grandTrafic = ParseBoolStr(properties["voiegrandtrafic"]?.ToString());

            Color roadColor = GetColorByCatDig(catDig, grandTrafic);
            float roadWidth = GetWidthByCatDig(catDig, grandTrafic);

            // ── Convert coordinates ───────────────────────────────────────
            List<Vector3> pts = new List<Vector3>();
            foreach (JToken c in coordinates)
            {
                JArray coord = c as JArray;
                if (coord == null || coord.Count < 2) continue;

                double lon = coord[0].Value<double>();
                double lat = coord[1].Value<double>();
                double h = coord.Count >= 3 ? coord[2].Value<double>() : 0.0;

                pts.Add(GeoToUnity(lon, lat, h));
            }

            if (pts.Count < 2) { skipped++; continue; }

            // ── Build graph edges ─────────────────────────────────────────
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int nA = GetOrCreateNode(pts[i]);
                int nB = GetOrCreateNode(pts[i + 1]);
                if (nA == nB) continue;

                BxEdge edge = new BxEdge
                {
                    id = edges.Count,
                    startNodeId = nA,
                    endNodeId = nB,
                    nomVoie = nomVoie,
                    catDig = catDig,
                    groupe = groupe,
                    grandTrafic = grandTrafic,
                    color = roadColor,
                    width = roadWidth,
                    points = new List<Vector3> { nodes[nA].position, nodes[nB].position },
                    lengthMeters = Vector3.Distance(nodes[nA].position, nodes[nB].position)
                };

                edges.Add(edge);
                nodes[nA].connectedEdgeIds.Add(edge.id);
                nodes[nB].connectedEdgeIds.Add(edge.id);
            }
        }

        Debug.Log($"[RoadDataManager] Loaded — nodes: {nodes.Count}  edges: {edges.Count}  skipped: {skipped}");
    }

    // ── Color by cat_dig ─────────────────────────────────────────────────────
    //
    //  cat_dig values in Bordeaux dataset:
    //  1 = autoroute / voie rapide   → نارنجی
    //  2 = route principale          → زرد
    //  3 = route secondaire          → سفید / خاکستری روشن
    //  4 = voie locale               → خاکستری
    //  5 = chemin / voie pietonne    → خاکستری تیره / سبز
    //
    // ─────────────────────────────────────────────────────────────────────────
    private Color GetColorByCatDig(int cat, bool grandTrafic)
    {
        if (grandTrafic) return new Color(1f, 0.42f, 0f);   // نارنجی - پرترافیک

        switch (cat)
        {
            case 1: return new Color(1f, 0.55f, 0f);    // نارنجی
            case 2: return new Color(1f, 0.85f, 0f);    // زرد
            case 3: return new Color(0.85f, 0.85f, 0.85f); // سفید خاکستری
            case 4: return new Color(0.55f, 0.55f, 0.55f); // خاکستری
            case 5: return new Color(0.35f, 0.65f, 0.35f); // سبز
            default: return new Color(0.5f, 0.5f, 0.5f);
        }
    }

    private float GetWidthByCatDig(int cat, bool grandTrafic)
    {
        if (grandTrafic) return 50f;

        switch (cat)
        {
            case 1: return 45f;
            case 2: return 32f;
            case 3: return 20f;
            case 4: return 12f;
            case 5: return 6f;
            default: return 10f;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetOrCreateNode(Vector3 position)
    {
        string key = MakeKey(position);
        if (nodeLookup.TryGetValue(key, out int id)) return id;

        int newId = nodes.Count;
        nodes.Add(new BxNode { id = newId, position = position });
        nodeLookup[key] = newId;
        return newId;
    }

    private string MakeKey(Vector3 p)
    {
        float t = Mathf.Max(0.01f, nodeMergeToleranceMeters);
        return $"{Mathf.RoundToInt(p.x / t)}_{Mathf.RoundToInt(p.z / t)}";
    }

    private Vector3 GeoToUnity(double lon, double lat, double height)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                            new double3(lon, lat, height));
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, 0f, (float)unity.z);
    }

    private static int ParseInt(string s, int fallback)
    {
        return int.TryParse(s, out int v) ? v : fallback;
    }

    private static bool ParseBoolStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Trim().ToLowerInvariant() == "true";
    }
}
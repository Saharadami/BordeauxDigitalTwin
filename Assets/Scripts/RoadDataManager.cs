using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class RoadSegment
{
    public string id;
    public string name;
    public string highway;
    public string surface;
    public bool oneway;
    public Color color;
    public float width;
    public List<Vector3> points = new List<Vector3>();
}

[Serializable]
public class RoadNode
{
    public int id;
    public Vector3 position;
    public List<int> connectedEdgeIds = new List<int>();
}

[Serializable]
public class RoadEdge
{
    public int id;
    public int startNodeId;
    public int endNodeId;
    public string roadId;
    public string name;
    public string highway;
    public string surface;
    public bool oneway;
    public float lengthMeters;
    public float width;
    public List<Vector3> points = new List<Vector3>();
    public Color color;
}

public class RoadDataManager : MonoBehaviour
{
    [Header("Input")]
    public string geoJsonFileName = "MainRoadPessac.geojson";
    public CesiumGeoreference georeference;

    [Header("Settings")]
    public float defaultHeightMeters = 0f;
    public bool loadOnStart = false;

    [Tooltip("Points closer than this value are treated as the same graph node.")]
    public float nodeMergeToleranceMeters = 0.75f;

    [Header("Output - Original Roads")]
    public List<RoadSegment> roads = new List<RoadSegment>();

    [Header("Output - Graph")]
    public List<RoadNode> nodes = new List<RoadNode>();
    public List<RoadEdge> edges = new List<RoadEdge>();

    private readonly Dictionary<string, int> nodeLookup = new Dictionary<string, int>();

    private void Start()
    {
        if (loadOnStart)
            LoadRoads();
    }

    [ContextMenu("Load Roads")]
    public void LoadRoads()
    {
        Debug.Log("LOAD ROADS STARTED");

        roads.Clear();
        nodes.Clear();
        edges.Clear();
        nodeLookup.Clear();

        if (georeference == null)
        {
            georeference = FindObjectOfType<CesiumGeoreference>();
            if (georeference == null)
            {
                Debug.LogError("CesiumGeoreference not found in scene.");
                return;
            }
        }

        string path = Path.Combine(Application.streamingAssetsPath, geoJsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogError("GeoJSON file not found at: " + path);
            return;
        }

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception e) { Debug.LogError("Failed to read GeoJSON file: " + e.Message); return; }

        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception e) { Debug.LogError("GeoJSON parse failed: " + e.Message); return; }

        JArray features = root["features"] as JArray;
        if (features == null)
        {
            Debug.LogError("No 'features' array found in GeoJSON.");
            return;
        }

        foreach (JToken featureToken in features)
        {
            JObject feature = featureToken as JObject;
            if (feature == null) continue;

            JObject properties = feature["properties"] as JObject;
            JObject geometry = feature["geometry"] as JObject;
            if (geometry == null) continue;

            string geometryType = geometry["type"]?.ToString();
            if (geometryType != "LineString") continue;

            JArray coordinates = geometry["coordinates"] as JArray;
            if (coordinates == null || coordinates.Count < 2) continue;

            string roadId = feature["id"]?.ToString();
            string highway = properties?["highway"]?.ToString();
            string name = properties?["name"]?.ToString();
            string surface = properties?["surface"]?.ToString();
            bool oneway = ParseBool(properties?["oneway"]?.ToString());

            Color roadColor = GetRoadColor(highway);
            float roadWidth = GetRoadWidth(highway);

            RoadSegment road = new RoadSegment
            {
                id = roadId,
                name = name,
                highway = highway,
                surface = surface,
                oneway = oneway,
                color = roadColor,
                width = roadWidth
            };

            for (int i = 0; i < coordinates.Count; i++)
            {
                JArray coord = coordinates[i] as JArray;
                if (coord == null || coord.Count < 2) continue;

                double lon = coord[0].Value<double>();
                double lat = coord[1].Value<double>();
                double h = coord.Count >= 3 ? coord[2].Value<double>() : defaultHeightMeters;

                Vector3 unityPoint = GeoToUnity(lon, lat, h);
                road.points.Add(unityPoint);
            }

            if (road.points.Count < 2) continue;

            roads.Add(road);

            for (int i = 0; i < road.points.Count - 1; i++)
            {
                Vector3 a = road.points[i];
                Vector3 b = road.points[i + 1];

                int nodeA = GetOrCreateNode(a);
                int nodeB = GetOrCreateNode(b);

                if (nodeA == nodeB) continue;

                RoadEdge edge = new RoadEdge
                {
                    id = edges.Count,
                    startNodeId = nodeA,
                    endNodeId = nodeB,
                    roadId = roadId,
                    name = name,
                    highway = highway,
                    surface = surface,
                    oneway = oneway,
                    color = roadColor,
                    width = roadWidth,
                    points = new List<Vector3> { nodes[nodeA].position, nodes[nodeB].position },
                    lengthMeters = Vector3.Distance(nodes[nodeA].position, nodes[nodeB].position)
                };

                edges.Add(edge);
                nodes[nodeA].connectedEdgeIds.Add(edge.id);
                nodes[nodeB].connectedEdgeIds.Add(edge.id);
            }
        }

        Debug.Log($"Loaded roads: {roads.Count}, nodes: {nodes.Count}, edges: {edges.Count}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  رنگ‌بندی واقعی‌تر بر اساس نوع جاده
    // ─────────────────────────────────────────────────────────────────────────
    private Color GetRoadColor(string highway)
    {
        string h = (highway ?? "").ToLowerInvariant();

        if (h.Contains("motorway")) return new Color(1f, 0.42f, 0f);    // نارنجی تیره
        if (h.Contains("trunk")) return new Color(1f, 0.6f, 0f);    // نارنجی روشن
        if (h.Contains("primary")) return new Color(1f, 0.85f, 0f);    // زرد
        if (h.Contains("secondary")) return new Color(0.9f, 0.9f, 0.9f); // سفید
        if (h.Contains("tertiary")) return new Color(0.7f, 0.7f, 0.7f); // خاکستری روشن
        if (h.Contains("residential") || h.Contains("living_street")) return new Color(0.5f, 0.5f, 0.5f); // خاکستری میانه
        if (h.Contains("service") || h.Contains("track")) return new Color(0.35f, 0.35f, 0.35f);// خاکستری تیره
        if (h.Contains("path") || h.Contains("footway") || h.Contains("cycleway")) return new Color(0.3f, 0.7f, 0.4f); // سبز

        return new Color(0.45f, 0.45f, 0.45f); // پیش‌فرض
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ضخامت متناسب با نوع جاده
    // ─────────────────────────────────────────────────────────────────────────
    public float GetRoadWidth(string highway)
    {
        string h = (highway ?? "").ToLowerInvariant();

        if (h.Contains("motorway")) return 60f;
        if (h.Contains("trunk")) return 50f;
        if (h.Contains("primary")) return 40f;
        if (h.Contains("secondary")) return 30f;
        if (h.Contains("tertiary")) return 20f;
        if (h.Contains("residential") || h.Contains("living_street")) return 12f;
        if (h.Contains("service")) return 8f;
        if (h.Contains("path") || h.Contains("footway") || h.Contains("cycleway")) return 5f;

        return 10f;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private int GetOrCreateNode(Vector3 position)
    {
        string key = MakeNodeKey(position);
        if (nodeLookup.TryGetValue(key, out int existingId))
            return existingId;

        int newId = nodes.Count;
        nodes.Add(new RoadNode { id = newId, position = position });
        nodeLookup[key] = newId;
        return newId;
    }

    private string MakeNodeKey(Vector3 position)
    {
        float t = Mathf.Max(0.01f, nodeMergeToleranceMeters);
        int x = Mathf.RoundToInt(position.x / t);
        int y = Mathf.RoundToInt(position.y / t);
        int z = Mathf.RoundToInt(position.z / t);
        return $"{x}_{y}_{z}";
    }

    private bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().ToLowerInvariant();
        return value == "yes" || value == "true" || value == "1";
    }

    private Vector3 GeoToUnity(double lon, double lat, double height)
    {
        double3 ecef =
            CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(lon, lat, height));

        double3 unity =
            georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);

        return new Vector3((float)unity.x, 0f, (float)unity.z);
    }
}
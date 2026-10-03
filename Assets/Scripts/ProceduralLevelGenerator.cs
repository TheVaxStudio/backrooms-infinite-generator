using System.Collections.Generic;
using UnityEngine;

public class ProceduralLevelGenerator : MonoBehaviour
{
    [Header("Geração")]
    [SerializeField] int chunkSize = 12;
    [SerializeField] int viewDistance = 2;
    [SerializeField] float cellSize = 2f;
    [SerializeField] float wallHeight = 16f;

    [Header("Player Prefab")]
    [SerializeField] GameObject playerPrefab;

    [Header("Estilo cartoon")]
    [SerializeField] Material floorMaterial;
    [SerializeField] Material wallMaterial;
    [SerializeField] Material ceilingMaterial;

    [Header("Lighting")]
    [SerializeField] Color lightColor = new Color(1f, 0.96f, 0.7f);
    [SerializeField] float lightRange = 35f;
    [SerializeField] float lightIntensity = 2.5f;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    Vector2Int currentChunk = new Vector2Int(int.MinValue, int.MinValue);

    GameObject player;
    bool playerSpawned;
    GameObject lightsContainer;

    void Awake()
    {
        if (floorMaterial == null || wallMaterial == null || ceilingMaterial == null)
        {
            CreateCartoonMaterials();
        }

        CreateLightingContainer();
        SpawnPlayer();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        RefreshChunksAroundWorldPosition(player.transform.position);
        SpawnBackroomsLights();
    }

    void Update()
    {
        UpdateChunkGeneration();
    }

    void CreateLightingContainer()
    {
        lightsContainer = new GameObject("BackroomsLights");
        lightsContainer.transform.SetParent(transform);
    }

    void SpawnBackroomsLights()
    {
        if (lightsContainer == null)
            return;

        GameObject lightObj1 = new GameObject("CeilingLight1");
        lightObj1.transform.SetParent(lightsContainer.transform);
        lightObj1.transform.localPosition = new Vector3(0f, wallHeight, 0f);

        Light light1 = lightObj1.AddComponent<Light>();
        light1.type = LightType.Point;
        light1.range = lightRange;
        light1.intensity = lightIntensity;
        light1.color = lightColor;
        light1.shadows = LightShadows.Soft;

        GameObject lightObj2 = new GameObject("CeilingLight2");
        lightObj2.transform.SetParent(lightsContainer.transform);
        lightObj2.transform.localPosition = new Vector3(15f, wallHeight, 15f);

        Light light2 = lightObj2.AddComponent<Light>();
        light2.type = LightType.Point;
        light2.range = lightRange * 0.9f;
        light2.intensity = lightIntensity * 0.8f;
        light2.color = lightColor;
        light2.shadows = LightShadows.Soft;

        GameObject lightObj3 = new GameObject("CeilingLight3");
        lightObj3.transform.SetParent(lightsContainer.transform);
        lightObj3.transform.localPosition = new Vector3(-15f, wallHeight, 15f);

        Light light3 = lightObj3.AddComponent<Light>();
        light3.type = LightType.Point;
        light3.range = lightRange * 0.85f;
        light3.intensity = lightIntensity * 0.75f;
        light3.color = lightColor;
        light3.shadows = LightShadows.Soft;
    }

    void SpawnPlayer()
    {
        if (playerPrefab != null)
        {
            player = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            player.name = "Player";
        }
        else
        {
            player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = Vector3.zero;
            player.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        }

        playerSpawned = true;
    }

    void UpdateChunkGeneration()
    {
        if (!playerSpawned || player == null)
            return;

        Vector3 playerPos = player.transform.position;
        Vector2Int chunkCoord = new Vector2Int(
            Mathf.FloorToInt(playerPos.x / (chunkSize * cellSize)),
            Mathf.FloorToInt(playerPos.z / (chunkSize * cellSize))
        );

        if (chunkCoord == currentChunk)
            return;

        currentChunk = chunkCoord;
        RefreshChunksAroundWorldPosition(playerPos);
    }

    void RefreshChunksAroundWorldPosition(Vector3 worldPos)
    {
        Vector2Int originChunk = new Vector2Int(
            Mathf.FloorToInt(worldPos.x / (chunkSize * cellSize)),
            Mathf.FloorToInt(worldPos.z / (chunkSize * cellSize))
        );

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                Vector2Int coord = new Vector2Int(originChunk.x + x, originChunk.y + z);

                if (!chunks.ContainsKey(coord))
                {
                    Chunk chunk = CreateChunk(coord);
                    chunks.Add(coord, chunk);
                }
            }
        }

        List<Vector2Int> remove = new List<Vector2Int>();
        foreach (var kvp in chunks)
        {
            Vector2Int c = kvp.Key;
            int dx = Mathf.Abs(c.x - originChunk.x);
            int dz = Mathf.Abs(c.y - originChunk.y);

            if (dx > viewDistance || dz > viewDistance)
            {
                remove.Add(c);
            }
        }

        foreach (var k in remove)
        {
            if (chunks.TryGetValue(k, out var chunk))
            {
                if (chunk.Root != null)
                    Destroy(chunk.Root);

                chunks.Remove(k);
            }
        }
    }

    Chunk CreateChunk(Vector2Int coord)
    {
        GameObject root = new GameObject($"Chunk_{coord.x}_{coord.y}");
        root.transform.SetParent(transform);

        MeshFilter mf = root.AddComponent<MeshFilter>();
        MeshRenderer mr = root.AddComponent<MeshRenderer>();
        MeshCollider mc = root.AddComponent<MeshCollider>();
        mc.convex = false;

        mr.materials = new[] { floorMaterial, wallMaterial, ceilingMaterial };

        Chunk chunk = new Chunk
        {
            Coord = coord,
            Root = root,
            MeshFilter = mf,
            MeshRenderer = mr,
            MeshCollider = mc
        };

        BuildChunkMesh(chunk);
        return chunk;
    }

    void BuildChunkMesh(Chunk chunk)
    {
        bool[,] labyrinth = GenerateLabyrinth(chunk.Coord);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>[] { new List<int>(), new List<int>(), new List<int>() };

        float minX = chunk.Coord.x * chunkSize * cellSize;
        float minZ = chunk.Coord.y * chunkSize * cellSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!labyrinth[x, z])
                    continue;

                float px = minX + x * cellSize;
                float pz = minZ + z * cellSize;

                Vector3 a = new Vector3(px, 0f, pz);
                Vector3 b = new Vector3(px + cellSize, 0f, pz);
                Vector3 c = new Vector3(px + cellSize, 0f, pz + cellSize);
                Vector3 d = new Vector3(px, 0f, pz + cellSize);

                AddQuadWithUV512(vertices, uvs, triangles, a, b, c, d, 0);

                Vector3 e = a + Vector3.up * wallHeight;
                Vector3 f = b + Vector3.up * wallHeight;
                Vector3 g = c + Vector3.up * wallHeight;
                Vector3 h = d + Vector3.up * wallHeight;

                AddQuadWithUV512(vertices, uvs, triangles, e, h, g, f, 2);
            }
        }

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!labyrinth[x, z])
                    continue;

                float px = minX + x * cellSize;
                float pz = minZ + z * cellSize;

                bool leftWall = x == 0 || !labyrinth[x - 1, z];
                bool rightWall = x == chunkSize - 1 || !labyrinth[x + 1, z];
                bool downWall = z == 0 || !labyrinth[x, z - 1];
                bool upWall = z == chunkSize - 1 || !labyrinth[x, z + 1];

                if (leftWall)
                {
                    Vector3 p0 = new Vector3(px, 0f, pz);
                    Vector3 p1 = new Vector3(px, 0f, pz + cellSize);
                    Vector3 p2 = new Vector3(px, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px, wallHeight, pz);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1);
                }

                if (rightWall)
                {
                    Vector3 p0 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1);
                }

                if (downWall)
                {
                    Vector3 p0 = new Vector3(px, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p3 = new Vector3(px, wallHeight, pz);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1);
                }

                if (upWall)
                {
                    Vector3 p0 = new Vector3(px, 0f, pz + cellSize);
                    Vector3 p1 = new Vector3(px, wallHeight, pz + cellSize);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1);
                }
            }
        }

        Mesh mesh = chunk.MeshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = $"BackroomsMesh_{chunk.Coord.x}_{chunk.Coord.y}";
            chunk.MeshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.subMeshCount = 3;

        mesh.SetTriangles(triangles[0], 0);
        mesh.SetTriangles(triangles[1], 1);
        mesh.SetTriangles(triangles[2], 2);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        chunk.MeshCollider.sharedMesh = null;
        chunk.MeshCollider.sharedMesh = mesh;
    }

    bool[,] GenerateLabyrinth(Vector2Int chunkCoord)
    {
        bool[,] grid = new bool[chunkSize, chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                grid[x, z] = false;
            }
        }

        int startX = chunkSize / 2;
        int startZ = chunkSize / 2;

        grid[startX, startZ] = true;

        int maxSteps = chunkSize * chunkSize * 8;
        Vector2Int current = new Vector2Int(startX, startZ);

        for (int i = 0; i < maxSteps; i++)
        {
            List<Vector2Int> dirs = new List<Vector2Int>
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            dirs = Shuffle(dirs);

            bool moved = false;

            for (int d = 0; d < dirs.Count; d++)
            {
                int nx = current.x + dirs[d].x;
                int nz = current.y + dirs[d].y;

                if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize)
                    continue;

                if (!grid[nx, nz])
                {
                    grid[nx, nz] = true;
                    current = new Vector2Int(nx, nz);
                    moved = true;
                    break;
                }
            }

            if (!moved)
            {
                if (Random.value < 0.35f)
                {
                    current = new Vector2Int(Random.Range(0, chunkSize), Random.Range(0, chunkSize));
                }
            }
        }

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!grid[x, z] && Random.value < 0.12f)
                {
                    grid[x, z] = true;
                }
            }
        }

        return grid;
    }

    List<Vector2Int> Shuffle(List<Vector2Int> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            Vector2Int temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }

        return list;
    }

    static void AddQuadWithUV512(List<Vector3> vertices, List<Vector2> uvs, List<int>[] triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int subMeshIndex)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 1);
        triangles[subMeshIndex].Add(start + 2);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 2);
        triangles[subMeshIndex].Add(start + 3);
    }

    void CreateCartoonMaterials()
    {
        Shader toonShader = Shader.Find("Room 700/URP/Toon Complete");
        if (toonShader == null)
            toonShader = Shader.Find("Universal Render Pipeline/Lit");

        Texture2D floorTex = GenerateFloorTexture();
        Texture2D wallTex = GenerateWallTexture();
        Texture2D ceilingTex = GenerateCeilingTexture();

        floorMaterial = new Material(toonShader);
        floorMaterial.mainTexture = floorTex;
        floorMaterial.color = new Color(0.78f, 0.73f, 0.56f);
        floorMaterial.SetFloat("_Metallic", 0f);
        floorMaterial.SetFloat("_Smoothness", 0f);

        wallMaterial = new Material(toonShader);
        wallMaterial.mainTexture = wallTex;
        wallMaterial.color = new Color(0.93f, 0.90f, 0.80f);
        wallMaterial.SetFloat("_Metallic", 0f);
        wallMaterial.SetFloat("_Smoothness", 0f);

        ceilingMaterial = new Material(toonShader);
        ceilingMaterial.mainTexture = ceilingTex;
        ceilingMaterial.color = new Color(0.97f, 0.95f, 0.88f);
        ceilingMaterial.SetFloat("_Metallic", 0f);
        ceilingMaterial.SetFloat("_Smoothness", 0f);
    }

    Texture2D GenerateFloorTexture()
    {
        Texture2D tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
        Color[] pixels = new Color[512 * 512];

        for (int y = 0; y < 512; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.02f, y * 0.02f);
                Color baseColor = Color.Lerp(
                    new Color(0.78f, 0.73f, 0.56f),
                    new Color(0.70f, 0.65f, 0.48f),
                    noise
                );

                if (Random.value < 0.04f)
                    baseColor = Color.Lerp(baseColor, new Color(0.55f, 0.50f, 0.40f), 0.4f);

                pixels[y * 512 + x] = baseColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    Texture2D GenerateWallTexture()
    {
        Texture2D tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
        Color[] pixels = new Color[512 * 512];

        for (int y = 0; y < 512; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.014f, y * 0.014f);
                Color baseColor = Color.Lerp(
                    new Color(0.93f, 0.90f, 0.80f),
                    new Color(0.88f, 0.84f, 0.72f),
                    noise
                );

                if (y % 64 < 4)
                    baseColor = Color.Lerp(baseColor, new Color(0.62f, 0.58f, 0.46f), 0.35f);

                if (Random.value < 0.06f)
                    baseColor = Color.Lerp(baseColor, new Color(0.47f, 0.44f, 0.35f), 0.45f);

                pixels[y * 512 + x] = baseColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    Texture2D GenerateCeilingTexture()
    {
        Texture2D tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
        Color[] pixels = new Color[512 * 512];

        for (int y = 0; y < 512; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.012f, y * 0.012f);
                Color baseColor = Color.Lerp(
                    new Color(0.97f, 0.95f, 0.88f),
                    new Color(0.92f, 0.90f, 0.82f),
                    noise
                );

                if ((x + y) % 128 < 8)
                    baseColor = new Color(1f, 0.98f, 0.86f);

                pixels[y * 512 + x] = baseColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    class Chunk
    {
        public Vector2Int Coord;
        public GameObject Root;
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
        public MeshCollider MeshCollider;
    }
}

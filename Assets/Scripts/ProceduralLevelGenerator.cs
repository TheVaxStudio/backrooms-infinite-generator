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

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    Vector2Int currentChunk = new Vector2Int(int.MinValue, int.MinValue);

    GameObject player;
    bool playerSpawned;

    void Awake()
    {
        if (floorMaterial == null || wallMaterial == null || ceilingMaterial == null)
        {
            CreateCartoonMaterials();
        }

        SpawnPlayer();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        RefreshChunksAroundWorldPosition(player.transform.position);
    }

    void Update()
    {
        UpdateChunkGeneration();
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

                AddQuadWithUV512(vertices, uvs, triangles, a, b, c, d, 0, x, z, chunk.Coord);

                Vector3 e = a + Vector3.up * wallHeight;
                Vector3 f = b + Vector3.up * wallHeight;
                Vector3 g = c + Vector3.up * wallHeight;
                Vector3 h = d + Vector3.up * wallHeight;

                AddQuadWithUV512(vertices, uvs, triangles, e, h, g, f, 2, x, z, chunk.Coord);
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

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1, x, z, chunk.Coord);
                }

                if (rightWall)
                {
                    Vector3 p0 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1, x, z, chunk.Coord);
                }

                if (downWall)
                {
                    Vector3 p0 = new Vector3(px, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p3 = new Vector3(px, wallHeight, pz);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1, x, z, chunk.Coord);
                }

                if (upWall)
                {
                    Vector3 p0 = new Vector3(px, 0f, pz + cellSize);
                    Vector3 p1 = new Vector3(px, wallHeight, pz + cellSize);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuadWithUV512(vertices, uvs, triangles, p0, p1, p2, p3, 1, x, z, chunk.Coord);
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

    static void AddQuadWithUV512(List<Vector3> vertices, List<Vector2> uvs, List<int>[] triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int subMeshIndex, int x, int z, Vector2Int chunkCoord)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        float uScale = 0.25f;
        float vScale = 0.25f;

        float uOffset = (x * uScale) % 1f;
        float vOffset = (z * vScale) % 1f;

        uvs.Add(new Vector2(uOffset, vOffset));
        uvs.Add(new Vector2(uOffset + uScale, vOffset));
        uvs.Add(new Vector2(uOffset + uScale, vOffset + vScale));
        uvs.Add(new Vector2(uOffset, vOffset + vScale));

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 1);
        triangles[subMeshIndex].Add(start + 2);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 2);
        triangles[subMeshIndex].Add(start + 3);
    }

    void CreateCartoonMaterials()
    {
        Texture2D floorTex = GenerateFloorTexture();
        Texture2D wallTex = GenerateWallTexture();
        Texture2D ceilTex = GenerateCeilingTexture();

        floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floorMaterial.mainTexture = floorTex;
        floorMaterial.color = new Color(0.78f, 0.73f, 0.56f);
        floorMaterial.SetFloat("_Metallic", 0f);
        floorMaterial.SetFloat("_Smoothness", 0f);

        wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wallMaterial.mainTexture = wallTex;
        wallMaterial.color = new Color(0.93f, 0.90f, 0.80f);
        wallMaterial.SetFloat("_Metallic", 0f);
        wallMaterial.SetFloat("_Smoothness", 0f);

        ceilingMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ceilingMaterial.mainTexture = ceilTex;
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
                float noise = Mathf.PerlinNoise(x * 0.01f, y * 0.01f);
                Color col = Color.Lerp(
                    new Color(0.78f, 0.73f, 0.56f),
                    new Color(0.70f, 0.65f, 0.48f),
                    noise
                );

                if (Random.value < 0.05f)
                    col = Color.Lerp(col, new Color(0.5f, 0.5f, 0.5f), 0.3f);

                pixels[y * 512 + x] = col;
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
                float noise = Mathf.PerlinNoise(x * 0.01f, y * 0.015f);
                Color col = Color.Lerp(
                    new Color(0.93f, 0.90f, 0.80f),
                    new Color(0.88f, 0.84f, 0.72f),
                    noise
                );

                if (y % 64 < 4)
                    col = Color.Lerp(col, new Color(0.6f, 0.55f, 0.4f), 0.4f);

                if (Random.value < 0.08f)
                    col = Color.Lerp(col, new Color(0.4f, 0.4f, 0.3f), 0.5f);

                pixels[y * 512 + x] = col;
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
                float noise = Mathf.PerlinNoise(x * 0.008f, y * 0.008f);
                Color col = Color.Lerp(
                    new Color(0.97f, 0.95f, 0.88f),
                    new Color(0.92f, 0.90f, 0.82f),
                    noise
                );

                if (x % 128 < 8 && y % 128 < 8)
                    col = new Color(1f, 0.98f, 0.85f);

                pixels[y * 512 + x] = col;
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

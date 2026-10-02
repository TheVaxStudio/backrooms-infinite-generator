using System.Collections.Generic;
using UnityEngine;

public class ProceduralLevelGenerator : MonoBehaviour
{
    [Header("Geração")]
    [SerializeField] int chunkSize = 10;
    [SerializeField] int viewDistance = 2;
    [SerializeField] float cellSize = 2f;
    [SerializeField] float wallHeight = 16f;

    [Header("Player Prefab")]
    [SerializeField] GameObject playerPrefab;

    [Header("Estilo cartoon")]
    [SerializeField] Material floorMaterial;
    [SerializeField] Material wallMaterial;
    [SerializeField] Material ceilingMaterial;

    [Header("Movimento")]
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float runSpeed = 10f;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    Vector2Int currentChunk = new Vector2Int(int.MinValue, int.MinValue);

    GameObject player;
    Vector3 moveInput;

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
        RefreshChunksAroundWorldPosition(Vector3.zero);
    }

    void Update()
    {
        HandlePlayerInput();
        UpdateChunkGeneration();
    }

    void FixedUpdate()
    {
        ApplyMovement();
    }

    void SpawnPlayer()
    {
        if (playerPrefab != null)
        {
            player = Instantiate(playerPrefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
            player.name = "Player";
        }
        else
        {
            player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 2f, 0f);
            player.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        }
    }

    void HandlePlayerInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(horizontal, 0f, vertical);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (Camera.main != null)
        {
            forward = Vector3.Scale(Camera.main.transform.forward, new Vector3(1f, 0f, 1f)).normalized;
            right = Vector3.Scale(Camera.main.transform.right, new Vector3(1f, 0f, 1f)).normalized;
        }

        moveInput = (forward * input.z + right * input.x).normalized;
    }

    void ApplyMovement()
    {
        if (player == null)
            return;

        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb == null)
            return;

        float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;

        Vector3 moveForce = moveInput * speed;
        moveForce.y = rb.velocity.y;

        rb.velocity = moveForce;

        if (moveInput.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveInput, Vector3.up);
            player.transform.rotation = Quaternion.Slerp(player.transform.rotation, targetRot, 12f * Time.deltaTime);
        }
    }

    void UpdateChunkGeneration()
    {
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
        bool[,] walkable = GenerateCorridorMap(chunk.Coord);

        var vertices = new List<Vector3>();
        var triangles = new List<int>[] { new List<int>(), new List<int>(), new List<int>() };

        float minX = chunk.Coord.x * chunkSize * cellSize;
        float minZ = chunk.Coord.y * chunkSize * cellSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!walkable[x, z])
                    continue;

                float px = minX + x * cellSize;
                float pz = minZ + z * cellSize;

                Vector3 a = new Vector3(px, 0f, pz);
                Vector3 b = new Vector3(px + cellSize, 0f, pz);
                Vector3 c = new Vector3(px + cellSize, 0f, pz + cellSize);
                Vector3 d = new Vector3(px, 0f, pz + cellSize);

                AddQuad(vertices, triangles, a, b, c, d, 0);

                Vector3 e = a + Vector3.up * wallHeight;
                Vector3 f = b + Vector3.up * wallHeight;
                Vector3 g = c + Vector3.up * wallHeight;
                Vector3 h = d + Vector3.up * wallHeight;

                AddQuad(vertices, triangles, e, h, g, f, 2);
            }
        }

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                if (!walkable[x, z])
                    continue;

                float px = minX + x * cellSize;
                float pz = minZ + z * cellSize;

                int leftX = x - 1;
                int rightX = x + 1;
                int downZ = z - 1;
                int upZ = z + 1;

                if (leftX < 0 || !walkable[leftX, z])
                {
                    Vector3 p0 = new Vector3(px, 0f, pz);
                    Vector3 p1 = new Vector3(px, 0f, pz + cellSize);
                    Vector3 p2 = new Vector3(px, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px, wallHeight, pz);

                    AddQuad(vertices, triangles, p0, p1, p2, p3, 1);
                }

                if (rightX >= chunkSize || !walkable[rightX, z])
                {
                    Vector3 p0 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuad(vertices, triangles, p0, p1, p2, p3, 1);
                }

                if (downZ < 0 || !walkable[x, downZ])
                {
                    Vector3 p0 = new Vector3(px, 0f, pz);
                    Vector3 p1 = new Vector3(px + cellSize, 0f, pz);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz);
                    Vector3 p3 = new Vector3(px, wallHeight, pz);

                    AddQuad(vertices, triangles, p0, p1, p2, p3, 1);
                }

                if (upZ >= chunkSize || !walkable[x, upZ])
                {
                    Vector3 p0 = new Vector3(px, 0f, pz + cellSize);
                    Vector3 p1 = new Vector3(px, wallHeight, pz + cellSize);
                    Vector3 p2 = new Vector3(px + cellSize, wallHeight, pz + cellSize);
                    Vector3 p3 = new Vector3(px + cellSize, 0f, pz + cellSize);

                    AddQuad(vertices, triangles, p0, p1, p2, p3, 1);
                }
            }
        }

        Mesh mesh = chunk.MeshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            chunk.MeshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.subMeshCount = 3;

        mesh.SetTriangles(triangles[0], 0);
        mesh.SetTriangles(triangles[1], 1);
        mesh.SetTriangles(triangles[2], 2);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        chunk.MeshCollider.sharedMesh = null;
        chunk.MeshCollider.sharedMesh = mesh;
    }

    bool[,] GenerateCorridorMap(Vector2Int chunkCoord)
    {
        bool[,] walkable = new bool[chunkSize, chunkSize];
        int centerX = chunkSize / 2;
        int centerZ = chunkSize / 2;

        Vector2Int current = new Vector2Int(centerX, centerZ);
        walkable[current.x, current.y] = true;

        int steps = chunkSize * chunkSize * 6;
        for (int i = 0; i < steps; i++)
        {
            int dir = Random.Range(0, 4);

            int nextX = current.x;
            int nextZ = current.y;

            if (dir == 0) nextX++;
            if (dir == 1) nextX--;
            if (dir == 2) nextZ++;
            if (dir == 3) nextZ--;

            if (nextX < 0 || nextX >= chunkSize || nextZ < 0 || nextZ >= chunkSize)
                continue;

            if (Random.value < 0.85f || walkable[nextX, nextZ] == false)
            {
                walkable[nextX, nextZ] = true;
                current = new Vector2Int(nextX, nextZ);
            }
        }

        int extraPasses = chunkSize * 2;
        for (int i = 0; i < extraPasses; i++)
        {
            int x = Random.Range(0, chunkSize);
            int z = Random.Range(0, chunkSize);

            if (!walkable[x, z])
            {
                walkable[x, z] = true;
            }
        }

        return walkable;
    }

    static void AddQuad(List<Vector3> vertices, List<int>[] triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int subMeshIndex)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 1);
        triangles[subMeshIndex].Add(start + 2);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 2);
        triangles[subMeshIndex].Add(start + 3);
    }

    void CreateCartoonMaterials()
    {
        floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = new Color(0.78f, 0.73f, 0.56f);
        floorMaterial.SetFloat("_Glossiness", 0f);
        floorMaterial.SetFloat("_Metallic", 0f);

        wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.color = new Color(0.93f, 0.90f, 0.80f);
        wallMaterial.SetFloat("_Glossiness", 0f);
        wallMaterial.SetFloat("_Metallic", 0f);

        ceilingMaterial = new Material(Shader.Find("Standard"));
        ceilingMaterial.color = new Color(0.97f, 0.95f, 0.88f);
        ceilingMaterial.SetFloat("_Glossiness", 0f);
        ceilingMaterial.SetFloat("_Metallic", 0f);
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

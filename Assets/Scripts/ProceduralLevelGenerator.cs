using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ProceduralLevelGenerator : MonoBehaviour
{
    [Header("Seed")]
    public bool randomSeed = true;
    public int seed = 12345;

    [Header("Mapa")]
    [Min(32)] public int mapWidth = 512;
    [Min(32)] public int mapHeight = 512;
    [Min(0.1f)] public float cellSize = 2f;
    [Min(1)] public int mapBorder = 8;

    [Header("Grandes Áreas")]
    [Min(1)] public int largeAreaCount = 24;
    [Min(8)] public int minAreaSize = 24;
    [Min(8)] public int maxAreaSize = 64;
    [Min(0)] public int areaCenterSpacing = 18;
    [Min(1)] public int areaPlacementAttempts = 100;

    [Header("Corredores")]
    [Range(2, 8)] public int corridorWidth = 2;
    [Min(0)] public int extraConnections = 14;
    [Range(0, 12)] public int corridorWander = 5;

    [Header("Irregularidade")]
    [Min(0)] public int irregularExpansionCount = 80;
    [Range(2, 16)] public int expansionMinSize = 3;
    [Range(2, 24)] public int expansionMaxSize = 10;

    [Header("Paredes Internas")]
    [Min(0)] public int wallIslandCount = 25;
    [Range(2, 8)] public int wallIslandMinSize = 2;
    [Range(2, 12)] public int wallIslandMaxSize = 5;

    [Header("Chunks")]
    [Min(4)] public int chunkSize = 32;
    [Min(0)] public int viewDistanceInChunks = 2;
    [Min(1)] public int unloadDistanceInChunks = 3;
    [Min(1)] public int maxLoadedChunks = 30;

    [Header("Geração Gradual")]
    [Min(1)] public int cellsPerFrame = 32;
    [Min(1)] public int maxChunkGenerationsPerFrame = 1;
    [Min(0.01f)] public float streamingInterval = 0.15f;

    [Header("Geometria")]
    [Min(0.1f)] public float wallHeight = 4f;
    [Min(0.01f)] public float wallThickness = 0.12f;
    public bool generateCeiling = true;
    [Min(0.1f)] public float ceilingHeight = 4f;

    [Header("Materiais da Mesh")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;

    [Header("Texturas")]
    public Texture2D floorTexture;
    public Texture2D wallTexture;
    public Texture2D ceilingTexture;

    [Header("UV / Escala das Texturas")]
    [Tooltip("Quantidade de repetições da textura por unidade de mundo no chão.")]
    [Min(0.01f)] public float floorTextureScale = 1f;

    [Tooltip("Quantidade de repetições da textura por unidade de mundo nas paredes.")]
    [Min(0.01f)] public float wallTextureScale = 1f;

    [Tooltip("Quantidade de repetições da textura por unidade de mundo no teto.")]
    [Min(0.01f)] public float ceilingTextureScale = 1f;

    [Header("Aplicar Texturas Automaticamente")]
    public bool applyTexturesToMaterials = true;

    [Header("Iluminação")]
    public bool generateLights = true;
    [Min(2)] public int lightSpacing = 8;
    public float lightIntensity = 2.5f;
    public float lightRange = 10f;
    public Color lightColor = new Color(1f, 0.82f, 0.55f);

    [Header("Player")]
    public GameObject playerPrefab;
    public float playerHeight = 1f;

    [Header("Debug")]
    public bool showDebug = false;

    private bool[,] floorMap;
    private System.Random rng;

    private readonly List<LevelArea> areas =
        new List<LevelArea>();

    private readonly Dictionary<Vector2Int, ChunkData> loadedChunks =
        new Dictionary<Vector2Int, ChunkData>();

    private readonly HashSet<Vector2Int> chunksBeingGenerated =
        new HashSet<Vector2Int>();

    private readonly Queue<Vector2Int> generationQueue =
        new Queue<Vector2Int>();

    private Transform generatedParent;
    private GameObject spawnedPlayer;

    private float streamingTimer;
    private bool levelReady;

    [Serializable]
    private class LevelArea
    {
        public RectInt rect;
        public Vector2Int center;

        public LevelArea(RectInt rect)
        {
            this.rect = rect;

            center = new Vector2Int(
                rect.xMin + rect.width / 2,
                rect.yMin + rect.height / 2
            );
        }
    }

    private class ChunkData
    {
        public Vector2Int coordinate;
        public GameObject root;

        public ChunkData(
            Vector2Int coordinate,
            GameObject root
        )
        {
            this.coordinate = coordinate;
            this.root = root;
        }
    }

    private void Start()
    {
        GenerateLevel();
    }

    private void Update()
    {
        if (!levelReady)
            return;

        if (spawnedPlayer == null)
            return;

        streamingTimer += Time.deltaTime;

        if (streamingTimer >= streamingInterval)
        {
            streamingTimer = 0f;
            UpdateChunkStreaming();
        }

        ProcessGenerationQueue();
    }

    [ContextMenu("Generate Level")]
    public void GenerateLevel()
    {
        StopAllCoroutines();

        levelReady = false;

        loadedChunks.Clear();
        chunksBeingGenerated.Clear();
        generationQueue.Clear();
        areas.Clear();

        if (generatedParent != null)
        {
            DestroyGeneratedLevel();
        }

        if (spawnedPlayer != null)
        {
            if (Application.isPlaying)
                Destroy(spawnedPlayer);
            else
                DestroyImmediate(spawnedPlayer);

            spawnedPlayer = null;
        }

        generatedParent =
            new GameObject("Generated Level").transform;

        generatedParent.SetParent(transform);

        if (randomSeed)
            seed = Environment.TickCount;

        rng = new System.Random(seed);

        floorMap =
            new bool[mapWidth, mapHeight];

        GenerateLargeAreas();
        ConnectAreas();
        GenerateIrregularExpansions();
        GenerateWallIslands();

        ApplyTextures();

        SpawnPlayer();

        levelReady = true;

        UpdateChunkStreaming();

        Debug.Log(
            $"Backrooms Level 0 gerado. Seed: {seed}"
        );
    }

    // =========================================================
    // TEXTURAS
    // =========================================================

    private void ApplyTextures()
    {
        if (!applyTexturesToMaterials)
            return;

        ApplyTextureToMaterial(
            floorMaterial,
            floorTexture
        );

        ApplyTextureToMaterial(
            wallMaterial,
            wallTexture
        );

        ApplyTextureToMaterial(
            ceilingMaterial,
            ceilingTexture
        );
    }

    private void ApplyTextureToMaterial(
        Material material,
        Texture2D texture
    )
    {
        if (material == null)
            return;

        if (texture == null)
            return;

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture(
                "_BaseMap",
                texture
            );
        }
        else if (material.HasProperty("_MainTex"))
        {
            material.SetTexture(
                "_MainTex",
                texture
            );
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor(
                "_BaseColor",
                Color.white
            );
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor(
                "_Color",
                Color.white
            );
        }

        texture.wrapMode =
            TextureWrapMode.Repeat;
    }

    // =========================================================
    // GERAÇÃO DO MAPA
    // =========================================================

    private void GenerateLargeAreas()
    {
        int attempts = 0;

        while (
            areas.Count < largeAreaCount &&
            attempts < areaPlacementAttempts
        )
        {
            attempts++;

            int width =
                RandomRange(
                    minAreaSize,
                    maxAreaSize + 1
                );

            int height =
                RandomRange(
                    minAreaSize,
                    maxAreaSize + 1
                );

            int minX = mapBorder;

            int maxX =
                mapWidth -
                width -
                mapBorder;

            int minY = mapBorder;

            int maxY =
                mapHeight -
                height -
                mapBorder;

            if (
                maxX <= minX ||
                maxY <= minY
            )
            {
                break;
            }

            int x =
                RandomRange(
                    minX,
                    maxX + 1
                );

            int y =
                RandomRange(
                    minY,
                    maxY + 1
                );

            RectInt rect =
                new RectInt(
                    x,
                    y,
                    width,
                    height
                );

            Vector2Int center =
                new Vector2Int(
                    rect.xMin +
                    rect.width / 2,

                    rect.yMin +
                    rect.height / 2
                );

            bool tooClose = false;

            for (
                int i = 0;
                i < areas.Count;
                i++
            )
            {
                if (
                    Vector2Int.Distance(
                        center,
                        areas[i].center
                    ) < areaCenterSpacing
                )
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            LevelArea area =
                new LevelArea(rect);

            areas.Add(area);

            CarveRect(rect);

            MakeAreaIrregular(rect);
        }

        if (areas.Count == 0)
        {
            int width =
                Mathf.Min(
                    maxAreaSize,
                    mapWidth -
                    mapBorder * 2
                );

            int height =
                Mathf.Min(
                    maxAreaSize,
                    mapHeight -
                    mapBorder * 2
                );

            RectInt fallback =
                new RectInt(
                    (mapWidth - width) / 2,
                    (mapHeight - height) / 2,
                    width,
                    height
                );

            areas.Add(
                new LevelArea(fallback)
            );

            CarveRect(fallback);
        }
    }

    private void MakeAreaIrregular(
        RectInt area
    )
    {
        int attempts =
            RandomRange(4, 10);

        for (
            int i = 0;
            i < attempts;
            i++
        )
        {
            int width =
                RandomRange(
                    expansionMinSize,
                    expansionMaxSize + 1
                );

            int height =
                RandomRange(
                    expansionMinSize,
                    expansionMaxSize + 1
                );

            int side =
                RandomRange(0, 4);

            RectInt extension;

            switch (side)
            {
                case 0:

                    extension =
                        new RectInt(
                            area.xMin -
                            width + 1,

                            RandomRange(
                                area.yMin,
                                Mathf.Max(
                                    area.yMin + 1,
                                    area.yMax - height
                                )
                            ),

                            width,
                            height
                        );

                    break;

                case 1:

                    extension =
                        new RectInt(
                            area.xMax - 1,

                            RandomRange(
                                area.yMin,
                                Mathf.Max(
                                    area.yMin + 1,
                                    area.yMax - height
                                )
                            ),

                            width,
                            height
                        );

                    break;

                case 2:

                    extension =
                        new RectInt(
                            RandomRange(
                                area.xMin,
                                Mathf.Max(
                                    area.xMin + 1,
                                    area.xMax - width
                                )
                            ),

                            area.yMin -
                            height + 1,

                            width,
                            height
                        );

                    break;

                default:

                    extension =
                        new RectInt(
                            RandomRange(
                                area.xMin,
                                Mathf.Max(
                                    area.xMin + 1,
                                    area.xMax - width
                                )
                            ),

                            area.yMax - 1,

                            width,
                            height
                        );

                    break;
            }

            CarveRectClamped(extension);
        }
    }

    private void ConnectAreas()
    {
        if (areas.Count <= 1)
            return;

        bool[] connected =
            new bool[areas.Count];

        connected[0] = true;

        int connections = 0;

        while (
            connections <
            areas.Count - 1
        )
        {
            float bestDistance =
                float.MaxValue;

            int bestA = -1;
            int bestB = -1;

            for (
                int a = 0;
                a < areas.Count;
                a++
            )
            {
                if (!connected[a])
                    continue;

                for (
                    int b = 0;
                    b < areas.Count;
                    b++
                )
                {
                    if (connected[b])
                        continue;

                    float distance =
                        (
                            areas[a].center -
                            areas[b].center
                        ).sqrMagnitude;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (
                bestA == -1 ||
                bestB == -1
            )
            {
                break;
            }

            CarveWanderingCorridor(
                areas[bestA].center,
                areas[bestB].center
            );

            connected[bestB] = true;
            connections++;
        }

        for (
            int i = 0;
            i < extraConnections;
            i++
        )
        {
            int a =
                RandomRange(
                    0,
                    areas.Count
                );

            int b =
                RandomRange(
                    0,
                    areas.Count
                );

            if (a == b)
                continue;

            CarveWanderingCorridor(
                areas[a].center,
                areas[b].center
            );
        }
    }

    private void CarveWanderingCorridor(
        Vector2Int start,
        Vector2Int end
    )
    {
        bool horizontalFirst =
            rng.NextDouble() > 0.5;

        int wanderX =
            RandomRange(
                -corridorWander,
                corridorWander + 1
            );

        int wanderY =
            RandomRange(
                -corridorWander,
                corridorWander + 1
            );

        if (horizontalFirst)
        {
            int bendX =
                Mathf.Clamp(
                    (start.x + end.x) / 2 +
                    wanderX,

                    mapBorder,

                    mapWidth -
                    mapBorder -
                    corridorWidth
                );

            int bendY =
                Mathf.Clamp(
                    end.y + wanderY,

                    mapBorder,

                    mapHeight -
                    mapBorder -
                    corridorWidth
                );

            CarveHorizontal(
                start.x,
                bendX,
                start.y
            );

            CarveVertical(
                start.y,
                bendY,
                bendX
            );

            CarveHorizontal(
                bendX,
                end.x,
                bendY
            );

            CarveVertical(
                bendY,
                end.y,
                end.x
            );
        }
        else
        {
            int bendY =
                Mathf.Clamp(
                    (start.y + end.y) / 2 +
                    wanderY,

                    mapBorder,

                    mapHeight -
                    mapBorder -
                    corridorWidth
                );

            int bendX =
                Mathf.Clamp(
                    end.x + wanderX,

                    mapBorder,

                    mapWidth -
                    mapBorder -
                    corridorWidth
                );

            CarveVertical(
                start.y,
                bendY,
                start.x
            );

            CarveHorizontal(
                start.x,
                bendX,
                bendY
            );

            CarveVertical(
                bendY,
                end.y,
                bendX
            );

            CarveHorizontal(
                bendX,
                end.x,
                end.y
            );
        }
    }

    private void CarveHorizontal(
        int x0,
        int x1,
        int y
    )
    {
        int minX =
            Mathf.Min(x0, x1);

        int maxX =
            Mathf.Max(x0, x1);

        for (
            int x = minX;
            x <= maxX;
            x++
        )
        {
            for (
                int w = 0;
                w < corridorWidth;
                w++
            )
            {
                SetFloor(
                    x,
                    y + w
                );
            }
        }
    }

    private void CarveVertical(
        int y0,
        int y1,
        int x
    )
    {
        int minY =
            Mathf.Min(y0, y1);

        int maxY =
            Mathf.Max(y0, y1);

        for (
            int y = minY;
            y <= maxY;
            y++
        )
        {
            for (
                int w = 0;
                w < corridorWidth;
                w++
            )
            {
                SetFloor(
                    x + w,
                    y
                );
            }
        }
    }

    private void GenerateIrregularExpansions()
    {
        for (
            int i = 0;
            i < irregularExpansionCount;
            i++
        )
        {
            int x =
                RandomRange(
                    mapBorder,
                    mapWidth - mapBorder
                );

            int y =
                RandomRange(
                    mapBorder,
                    mapHeight - mapBorder
                );

            if (!HasFloor(x, y))
                continue;

            int width =
                RandomRange(
                    expansionMinSize,
                    expansionMaxSize + 1
                );

            int height =
                RandomRange(
                    expansionMinSize,
                    expansionMaxSize + 1
                );

            int direction =
                RandomRange(0, 4);

            RectInt expansion;

            switch (direction)
            {
                case 0:

                    expansion =
                        new RectInt(
                            x - width,
                            y - height / 2,
                            width,
                            height
                        );

                    break;

                case 1:

                    expansion =
                        new RectInt(
                            x,
                            y - height / 2,
                            width,
                            height
                        );

                    break;

                case 2:

                    expansion =
                        new RectInt(
                            x - width / 2,
                            y - height,
                            width,
                            height
                        );

                    break;

                default:

                    expansion =
                        new RectInt(
                            x - width / 2,
                            y,
                            width,
                            height
                        );

                    break;
            }

            CarveRectClamped(
                expansion
            );
        }
    }

    private void GenerateWallIslands()
    {
        int remaining =
            wallIslandCount;

        int maxAttempts =
            wallIslandCount * 5;

        for (
            int attempt = 0;
            attempt < maxAttempts &&
            remaining > 0;
            attempt++
        )
        {
            int width =
                RandomRange(
                    wallIslandMinSize,
                    wallIslandMaxSize + 1
                );

            int height =
                RandomRange(
                    wallIslandMinSize,
                    wallIslandMaxSize + 1
                );

            int x =
                RandomRange(
                    mapBorder + 2,

                    Mathf.Max(
                        mapBorder + 3,
                        mapWidth -
                        mapBorder -
                        width -
                        2
                    )
                );

            int y =
                RandomRange(
                    mapBorder + 2,

                    Mathf.Max(
                        mapBorder + 3,
                        mapHeight -
                        mapBorder -
                        height -
                        2
                    )
                );

            RectInt island =
                new RectInt(
                    x,
                    y,
                    width,
                    height
                );

            if (!CanCreateWallIsland(island))
                continue;

            ClearRect(island);

            remaining--;
        }
    }

    private bool CanCreateWallIsland(
        RectInt rect
    )
    {
        for (
            int y = rect.yMin - 1;
            y <= rect.yMax;
            y++
        )
        {
            for (
                int x = rect.xMin - 1;
                x <= rect.xMax;
                x++
            )
            {
                if (!InBounds(x, y))
                    return false;

                if (!HasFloor(x, y))
                    return false;
            }
        }

        return true;
    }

    private void CarveRect(
        RectInt rect
    )
    {
        for (
            int y = rect.yMin;
            y < rect.yMax;
            y++
        )
        {
            for (
                int x = rect.xMin;
                x < rect.xMax;
                x++
            )
            {
                SetFloor(x, y);
            }
        }
    }

    private void CarveRectClamped(
        RectInt rect
    )
    {
        int minX =
            Mathf.Max(
                mapBorder,
                rect.xMin
            );

        int maxX =
            Mathf.Min(
                mapWidth - mapBorder,
                rect.xMax
            );

        int minY =
            Mathf.Max(
                mapBorder,
                rect.yMin
            );

        int maxY =
            Mathf.Min(
                mapHeight - mapBorder,
                rect.yMax
            );

        for (
            int y = minY;
            y < maxY;
            y++
        )
        {
            for (
                int x = minX;
                x < maxX;
                x++
            )
            {
                floorMap[x, y] = true;
            }
        }
    }

    private void ClearRect(
        RectInt rect
    )
    {
        for (
            int y = rect.yMin;
            y < rect.yMax;
            y++
        )
        {
            for (
                int x = rect.xMin;
                x < rect.xMax;
                x++
            )
            {
                if (InBounds(x, y))
                    floorMap[x, y] = false;
            }
        }
    }

    private void SetFloor(
        int x,
        int y
    )
    {
        if (!InBounds(x, y))
            return;

        floorMap[x, y] = true;
    }

    private bool HasFloor(
        int x,
        int y
    )
    {
        if (!InBounds(x, y))
            return false;

        return floorMap[x, y];
    }

    private bool InBounds(
        int x,
        int y
    )
    {
        return
            x >= 0 &&
            y >= 0 &&
            x < mapWidth &&
            y < mapHeight;
    }

    private int RandomRange(
        int min,
        int max
    )
    {
        return rng.Next(min, max);
    }

    // =========================================================
    // PLAYER
    // =========================================================

    private void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError(
                "ProceduralLevelGenerator: Player Prefab não foi definido."
            );

            return;
        }

        if (areas.Count == 0)
            return;

        Vector2Int spawnCell =
            areas[0].center;

        if (!HasFloor(
            spawnCell.x,
            spawnCell.y
        ))
        {
            bool found = false;

            for (
                int radius = 1;
                radius < 20 &&
                !found;
                radius++
            )
            {
                for (
                    int y = -radius;
                    y <= radius;
                    y++
                )
                {
                    for (
                        int x = -radius;
                        x <= radius;
                        x++
                    )
                    {
                        int px =
                            spawnCell.x + x;

                        int py =
                            spawnCell.y + y;

                        if (!InBounds(px, py))
                            continue;

                        if (HasFloor(px, py))
                        {
                            spawnCell =
                                new Vector2Int(
                                    px,
                                    py
                                );

                            found = true;
                            break;
                        }
                    }

                    if (found)
                        break;
                }
            }
        }

        Vector3 spawnPosition =
            CellToWorld(
                spawnCell,
                playerHeight
            );

        spawnedPlayer =
            Instantiate(
                playerPrefab,
                spawnPosition,
                Quaternion.identity
            );

        spawnedPlayer.name =
            "Player";
    }

    // =========================================================
    // STREAMING
    // =========================================================

    private void UpdateChunkStreaming()
    {
        if (spawnedPlayer == null)
            return;

        Vector2Int playerChunk =
            WorldToChunk(
                spawnedPlayer.transform.position
            );

        HashSet<Vector2Int> neededChunks =
            new HashSet<Vector2Int>();

        for (
            int y = -viewDistanceInChunks;
            y <= viewDistanceInChunks;
            y++
        )
        {
            for (
                int x = -viewDistanceInChunks;
                x <= viewDistanceInChunks;
                x++
            )
            {
                Vector2Int chunk =
                    playerChunk +
                    new Vector2Int(x, y);

                if (!ChunkInsideMap(chunk))
                    continue;

                neededChunks.Add(chunk);
            }
        }

        foreach (
            Vector2Int chunk
            in neededChunks
        )
        {
            if (loadedChunks.ContainsKey(chunk))
                continue;

            if (chunksBeingGenerated.Contains(chunk))
                continue;

            if (generationQueue.Contains(chunk))
                continue;

            if (
                loadedChunks.Count +
                chunksBeingGenerated.Count +
                generationQueue.Count >=
                maxLoadedChunks
            )
            {
                continue;
            }

            generationQueue.Enqueue(chunk);
        }

        UnloadFarChunks(
            playerChunk
        );
    }

    private void ProcessGenerationQueue()
    {
        int amount =
            Mathf.Max(
                1,
                maxChunkGenerationsPerFrame
            );

        for (
            int i = 0;
            i < amount;
            i++
        )
        {
            if (generationQueue.Count == 0)
                break;

            Vector2Int chunk =
                generationQueue.Dequeue();

            if (loadedChunks.ContainsKey(chunk))
                continue;

            if (chunksBeingGenerated.Contains(chunk))
                continue;

            if (!IsChunkStillNeeded(chunk))
                continue;

            chunksBeingGenerated.Add(chunk);

            StartCoroutine(
                GenerateChunkCoroutine(
                    chunk
                )
            );
        }
    }

    // =========================================================
    // CHUNK / MESH
    // =========================================================

    private IEnumerator GenerateChunkCoroutine(
        Vector2Int chunkCoordinate
    )
    {
        GameObject chunkRoot =
            new GameObject(
                $"Chunk_{chunkCoordinate.x}_{chunkCoordinate.y}"
            );

        chunkRoot.transform.SetParent(
            generatedParent
        );

        MeshFilter meshFilter =
            chunkRoot.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer =
            chunkRoot.AddComponent<MeshRenderer>();

        MeshCollider meshCollider =
            chunkRoot.AddComponent<MeshCollider>();

        List<Vector3> vertices =
            new List<Vector3>();

        List<int> floorTriangles =
            new List<int>();

        List<int> wallTriangles =
            new List<int>();

        List<int> ceilingTriangles =
            new List<int>();

        List<Vector2> uvs =
            new List<Vector2>();

        int startX =
            chunkCoordinate.x *
            chunkSize;

        int startY =
            chunkCoordinate.y *
            chunkSize;

        int endX =
            Mathf.Min(
                startX + chunkSize,
                mapWidth
            );

        int endY =
            Mathf.Min(
                startY + chunkSize,
                mapHeight
            );

        int processedCells = 0;

        for (
            int y = startY;
            y < endY;
            y++
        )
        {
            for (
                int x = startX;
                x < endX;
                x++
            )
            {
                if (HasFloor(x, y))
                {
                    AddFloorCell(
                        x,
                        y,
                        vertices,
                        floorTriangles,
                        uvs
                    );

                    AddBoundaryWalls(
                        x,
                        y,
                        vertices,
                        wallTriangles,
                        uvs
                    );

                    if (generateCeiling)
                    {
                        AddCeilingCell(
                            x,
                            y,
                            vertices,
                            ceilingTriangles,
                            uvs
                        );
                    }
                }

                processedCells++;

                if (
                    processedCells >=
                    cellsPerFrame
                )
                {
                    processedCells = 0;
                    yield return null;
                }
            }
        }

        Mesh mesh =
            new Mesh();

        mesh.name =
            $"LevelMesh_{chunkCoordinate.x}_{chunkCoordinate.y}";

        mesh.indexFormat =
            IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);

        mesh.subMeshCount =
            generateCeiling ? 3 : 2;

        mesh.SetTriangles(
            floorTriangles,
            0
        );

        mesh.SetTriangles(
            wallTriangles,
            1
        );

        if (generateCeiling)
        {
            mesh.SetTriangles(
                ceilingTriangles,
                2
            );
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.sharedMesh =
            mesh;

        if (generateCeiling)
        {
            meshRenderer.sharedMaterials =
                new Material[]
                {
                    floorMaterial,
                    wallMaterial,
                    ceilingMaterial
                };
        }
        else
        {
            meshRenderer.sharedMaterials =
                new Material[]
                {
                    floorMaterial,
                    wallMaterial
                };
        }

        meshCollider.sharedMesh =
            mesh;

        yield return null;

        if (generateLights)
        {
            GenerateChunkLights(
                chunkRoot.transform,
                startX,
                startY,
                endX,
                endY
            );
        }

        loadedChunks.Add(
            chunkCoordinate,
            new ChunkData(
                chunkCoordinate,
                chunkRoot
            )
        );

        chunksBeingGenerated.Remove(
            chunkCoordinate
        );
    }

    // =========================================================
    // CHÃO
    // =========================================================

    private void AddFloorCell(
        int x,
        int y,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        float worldX =
            x * cellSize;

        float worldZ =
            y * cellSize;

        int index =
            vertices.Count;

        vertices.Add(
            new Vector3(
                worldX,
                0f,
                worldZ
            )
        );

        vertices.Add(
            new Vector3(
                worldX + cellSize,
                0f,
                worldZ
            )
        );

        vertices.Add(
            new Vector3(
                worldX + cellSize,
                0f,
                worldZ + cellSize
            )
        );

        vertices.Add(
            new Vector3(
                worldX,
                0f,
                worldZ + cellSize
            )
        );

        triangles.Add(index + 0);
        triangles.Add(index + 2);
        triangles.Add(index + 1);

        triangles.Add(index + 0);
        triangles.Add(index + 3);
        triangles.Add(index + 2);

        float u0 =
            worldX * floorTextureScale;

        float u1 =
            (worldX + cellSize) *
            floorTextureScale;

        float v0 =
            worldZ * floorTextureScale;

        float v1 =
            (worldZ + cellSize) *
            floorTextureScale;

        uvs.Add(
            new Vector2(
                u0,
                v0
            )
        );

        uvs.Add(
            new Vector2(
                u1,
                v0
            )
        );

        uvs.Add(
            new Vector2(
                u1,
                v1
            )
        );

        uvs.Add(
            new Vector2(
                u0,
                v1
            )
        );
    }

    // =========================================================
    // TETO
    // =========================================================

    private void AddCeilingCell(
        int x,
        int y,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        float worldX =
            x * cellSize;

        float worldZ =
            y * cellSize;

        int index =
            vertices.Count;

        vertices.Add(
            new Vector3(
                worldX,
                ceilingHeight,
                worldZ
            )
        );

        vertices.Add(
            new Vector3(
                worldX,
                ceilingHeight,
                worldZ + cellSize
            )
        );

        vertices.Add(
            new Vector3(
                worldX + cellSize,
                ceilingHeight,
                worldZ + cellSize
            )
        );

        vertices.Add(
            new Vector3(
                worldX + cellSize,
                ceilingHeight,
                worldZ
            )
        );

        triangles.Add(index + 0);
        triangles.Add(index + 2);
        triangles.Add(index + 1);

        triangles.Add(index + 0);
        triangles.Add(index + 3);
        triangles.Add(index + 2);

        float u0 =
            worldX * ceilingTextureScale;

        float u1 =
            (worldX + cellSize) *
            ceilingTextureScale;

        float v0 =
            worldZ * ceilingTextureScale;

        float v1 =
            (worldZ + cellSize) *
            ceilingTextureScale;

        uvs.Add(
            new Vector2(
                u0,
                v0
            )
        );

        uvs.Add(
            new Vector2(
                u0,
                v1
            )
        );

        uvs.Add(
            new Vector2(
                u1,
                v1
            )
        );

        uvs.Add(
            new Vector2(
                u1,
                v0
            )
        );
    }

    // =========================================================
    // PAREDES
    // =========================================================

    private void AddBoundaryWalls(
        int x,
        int y,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        if (!HasFloor(x, y + 1))
        {
            AddWallBox(
                new Vector3(
                    x * cellSize,
                    0f,
                    (y + 1) * cellSize
                ),

                new Vector3(
                    (x + 1) * cellSize,
                    0f,
                    (y + 1) * cellSize
                ),

                vertices,
                triangles,
                uvs
            );
        }

        if (!HasFloor(x, y - 1))
        {
            AddWallBox(
                new Vector3(
                    (x + 1) * cellSize,
                    0f,
                    y * cellSize
                ),

                new Vector3(
                    x * cellSize,
                    0f,
                    y * cellSize
                ),

                vertices,
                triangles,
                uvs
            );
        }

        if (!HasFloor(x - 1, y))
        {
            AddWallBox(
                new Vector3(
                    x * cellSize,
                    0f,
                    y * cellSize
                ),

                new Vector3(
                    x * cellSize,
                    0f,
                    (y + 1) * cellSize
                ),

                vertices,
                triangles,
                uvs
            );
        }

        if (!HasFloor(x + 1, y))
        {
            AddWallBox(
                new Vector3(
                    (x + 1) * cellSize,
                    0f,
                    (y + 1) * cellSize
                ),

                new Vector3(
                    (x + 1) * cellSize,
                    0f,
                    y * cellSize
                ),

                vertices,
                triangles,
                uvs
            );
        }
    }

    private void AddWallBox(
        Vector3 start,
        Vector3 end,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        Vector3 direction =
            (end - start).normalized;

        Vector3 normal =
            new Vector3(
                -direction.z,
                0f,
                direction.x
            );

        Vector3 thickness =
            normal *
            (wallThickness * 0.5f);

        Vector3 bottomA =
            start - thickness;

        Vector3 bottomB =
            end - thickness;

        Vector3 bottomC =
            end + thickness;

        Vector3 bottomD =
            start + thickness;

        Vector3 topA =
            bottomA +
            Vector3.up *
            wallHeight;

        Vector3 topB =
            bottomB +
            Vector3.up *
            wallHeight;

        Vector3 topC =
            bottomC +
            Vector3.up *
            wallHeight;

        Vector3 topD =
            bottomD +
            Vector3.up *
            wallHeight;

        float wallLength =
            Vector3.Distance(
                start,
                end
            );

        float horizontalTiling =
            wallLength *
            wallTextureScale;

        float verticalTiling =
            wallHeight *
            wallTextureScale;

        float thicknessTiling =
            wallThickness *
            wallTextureScale;

        // -----------------------------------------------------
        // FACE PRINCIPAL
        // -----------------------------------------------------

        AddTexturedQuad(
            bottomA,
            bottomB,
            topB,
            topA,

            horizontalTiling,
            verticalTiling,

            vertices,
            triangles,
            uvs
        );

        // -----------------------------------------------------
        // FACE OPOSTA
        // -----------------------------------------------------

        AddTexturedQuad(
            bottomD,
            topD,
            topC,
            bottomC,

            horizontalTiling,
            verticalTiling,

            vertices,
            triangles,
            uvs
        );

        // -----------------------------------------------------
        // TOPO DA PAREDE
        // -----------------------------------------------------

        AddTexturedQuad(
            topA,
            topB,
            topC,
            topD,

            horizontalTiling,
            thicknessTiling,

            vertices,
            triangles,
            uvs
        );

        // -----------------------------------------------------
        // FUNDO DA PAREDE
        // -----------------------------------------------------

        AddTexturedQuad(
            bottomD,
            bottomC,
            bottomB,
            bottomA,

            horizontalTiling,
            thicknessTiling,

            vertices,
            triangles,
            uvs
        );

        // -----------------------------------------------------
        // EXTREMIDADE A
        // -----------------------------------------------------

        AddTexturedQuad(
            bottomA,
            topA,
            topD,
            bottomD,

            thicknessTiling,
            verticalTiling,

            vertices,
            triangles,
            uvs
        );

        // -----------------------------------------------------
        // EXTREMIDADE B
        // -----------------------------------------------------

        AddTexturedQuad(
            bottomB,
            bottomC,
            topC,
            topB,

            thicknessTiling,
            verticalTiling,

            vertices,
            triangles,
            uvs
        );
    }

    // =========================================================
    // QUAD COM UV CORRETA
    // =========================================================

    private void AddTexturedQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uSize,
        float vSize,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs
    )
    {
        int index =
            vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles.Add(index + 0);
        triangles.Add(index + 1);
        triangles.Add(index + 2);

        triangles.Add(index + 0);
        triangles.Add(index + 2);
        triangles.Add(index + 3);

        uvs.Add(
            new Vector2(
                0f,
                0f
            )
        );

        uvs.Add(
            new Vector2(
                uSize,
                0f
            )
        );

        uvs.Add(
            new Vector2(
                uSize,
                vSize
            )
        );

        uvs.Add(
            new Vector2(
                0f,
                vSize
            )
        );
    }

    // =========================================================
    // ILUMINAÇÃO
    // =========================================================

    private void GenerateChunkLights(
        Transform parent,
        int startX,
        int startY,
        int endX,
        int endY
    )
    {
        for (
            int y = startY;
            y < endY;
            y += lightSpacing
        )
        {
            for (
                int x = startX;
                x < endX;
                x += lightSpacing
            )
            {
                if (!HasFloor(x, y))
                    continue;

                if (!HasFloor(x + 1, y))
                    continue;

                if (!HasFloor(x, y + 1))
                    continue;

                GameObject lightObject =
                    new GameObject(
                        "Backrooms Light"
                    );

                lightObject.transform.SetParent(
                    parent
                );

                lightObject.transform.position =
                    CellToWorld(
                        new Vector2Int(x, y),
                        ceilingHeight - 0.15f
                    );

                Light light =
                    lightObject.AddComponent<Light>();

                light.type =
                    LightType.Point;

                light.color =
                    lightColor;

                light.intensity =
                    lightIntensity;

                light.range =
                    lightRange;

                light.shadows =
                    LightShadows.Soft;
            }
        }
    }

    // =========================================================
    // STREAMING AUXILIAR
    // =========================================================

    private bool IsChunkStillNeeded(
        Vector2Int chunk
    )
    {
        if (spawnedPlayer == null)
            return false;

        Vector2Int playerChunk =
            WorldToChunk(
                spawnedPlayer.transform.position
            );

        int dx =
            Mathf.Abs(
                chunk.x -
                playerChunk.x
            );

        int dy =
            Mathf.Abs(
                chunk.y -
                playerChunk.y
            );

        return
            dx <= viewDistanceInChunks &&
            dy <= viewDistanceInChunks;
    }

    private void UnloadFarChunks(
        Vector2Int playerChunk
    )
    {
        List<Vector2Int> remove =
            new List<Vector2Int>();

        foreach (
            KeyValuePair<Vector2Int, ChunkData> pair
            in loadedChunks
        )
        {
            Vector2Int chunk =
                pair.Key;

            int dx =
                Mathf.Abs(
                    chunk.x -
                    playerChunk.x
                );

            int dy =
                Mathf.Abs(
                    chunk.y -
                    playerChunk.y
                );

            if (
                dx > unloadDistanceInChunks ||
                dy > unloadDistanceInChunks
            )
            {
                remove.Add(chunk);
            }
        }

        for (
            int i = 0;
            i < remove.Count;
            i++
        )
        {
            Vector2Int coordinate =
                remove[i];

            if (
                loadedChunks.TryGetValue(
                    coordinate,
                    out ChunkData data
                )
            )
            {
                if (data.root != null)
                    Destroy(data.root);
            }

            loadedChunks.Remove(
                coordinate
            );
        }
    }

    private bool ChunkInsideMap(
        Vector2Int chunk
    )
    {
        int maxChunkX =
            Mathf.CeilToInt(
                (float)mapWidth /
                chunkSize
            );

        int maxChunkY =
            Mathf.CeilToInt(
                (float)mapHeight /
                chunkSize
            );

        return
            chunk.x >= 0 &&
            chunk.y >= 0 &&
            chunk.x < maxChunkX &&
            chunk.y < maxChunkY;
    }

    private Vector2Int WorldToChunk(
        Vector3 worldPosition
    )
    {
        int cellX =
            Mathf.FloorToInt(
                worldPosition.x /
                cellSize
            );

        int cellY =
            Mathf.FloorToInt(
                worldPosition.z /
                cellSize
            );

        return new Vector2Int(
            Mathf.FloorToInt(
                (float)cellX /
                chunkSize
            ),
            Mathf.FloorToInt(
                (float)cellY /
                chunkSize
            )
        );
    }

    private Vector3 CellToWorld(
        Vector2Int cell,
        float y
    )
    {
        return new Vector3(
            (cell.x + 0.5f) *
            cellSize,

            y,

            (cell.y + 0.5f) *
            cellSize
        );
    }

    private void DestroyGeneratedLevel()
    {
        if (generatedParent == null)
            return;

        if (Application.isPlaying)
        {
            Destroy(
                generatedParent.gameObject
            );
        }
        else
        {
            DestroyImmediate(
                generatedParent.gameObject
            );
        }

        generatedParent = null;
    }

    // =========================================================
    // DEBUG
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        if (!showDebug)
            return;

        Gizmos.color =
            Color.yellow;

        Vector3 size =
            new Vector3(
                mapWidth * cellSize,
                0.1f,
                mapHeight * cellSize
            );

        Vector3 center =
            new Vector3(
                mapWidth *
                cellSize *
                0.5f,

                0f,

                mapHeight *
                cellSize *
                0.5f
            );

        Gizmos.DrawWireCube(
            center,
            size
        );
    }
}

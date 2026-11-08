using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SleepDefenders.EditorTools
{
    /// <summary>
    /// Constrói o jogo inteiro a partir dos scripts e da arte gerada (Tripo AI + Nano Banana):
    ///   1. Tags e Layers            5. Prefabs dos brinquedos e monstros (com o modelo .glb do Tripo)
    ///   2. TMP Essentials           6. ToyDefinitions (cards)
    ///   3. Import das texturas      7. Cena "Game" com quarto, luzes, grade, gerenciadores e UI
    ///   4. Materiais URP            8. Build Settings
    /// É idempotente: pode rodar de novo a qualquer momento (ex.: após regerar modelos).
    /// Se um modelo .glb não existir, usa um placeholder de primitivas para o jogo continuar jogável.
    /// </summary>
    public static class SleepDefendersBuilder
    {
        public const string Root = "Assets/SleepDefenders";
        private const string Gen = Root + "/Generated";
        private const string ModelsDir = Root + "/Art/Models";
        private const string IconsDir = Root + "/Art/Icons";
        private const string TexturesDir = Root + "/Art/Textures";
        private const string UIArtDir = Root + "/Art/UI";
        public const string ScenePath = Root + "/Scenes/Game.unity";
        public const string CampaignPath = Gen + "/Data/Campanha.asset";
        private const string LaneGuardPrefabPath = Gen + "/Prefabs/LaneGuard_RatinhoDeCorda.prefab";

        // Orientação dos modelos do Tripo: os .glb exportados pelo Tripo (export_orientation "+x"),
        // importados pelo glTFast, ficam com a FRENTE apontando para -X no Unity.
        //   - Brinquedos precisam olhar para +X (de onde vêm os monstros): yaw 180.
        //   - Monstros precisam olhar para -X (rumo à cama): yaw 0.
        // Um pequeno giro extra vira os modelos em 3/4 para a câmera (que fica em -Z), como em jogos do gênero.
        private const float TurnTowardCamera = 25f;
        private const float ToyYaw = 180f + TurnTowardCamera;
        private const float EnemyYaw = -TurnTowardCamera;

        private static readonly List<string> missingModels = new List<string>();
        private static TMP_FontAsset font;
        private static Material fontOutlineMaterial;

        // ======================================================================= MENU

        [MenuItem("Sleep Defenders/Construir Jogo (prefabs + cena)", priority = 0)]
        public static void BuildFromMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Sleep Defenders",
                    "Isso recria os prefabs e a cena Game.unity (alterações manuais nela serão perdidas). Continuar?",
                    "Construir", "Cancelar"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAll();
            EditorSceneManager.OpenScene(ScenePath);

            string msg = "Pronto! Aperte Play para jogar.\n\nCena: " + ScenePath;
            if (missingModels.Count > 0)
                msg += "\n\nModelos ausentes (usando placeholders): " + string.Join(", ", missingModels) +
                       "\nRode Tools/AssetPipeline/generate_assets.py models e construa de novo.";
            EditorUtility.DisplayDialog("Sleep Defenders", msg, "OK");
        }

        [MenuItem("Sleep Defenders/Abrir Cena do Jogo", priority = 1)]
        public static void OpenGameScene()
        {
            if (File.Exists(ScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Sleep Defenders/Progresso/Liberar Todas as Noites", priority = 20)]
        public static void UnlockAllNights()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<Campaign>(CampaignPath);
            LevelProgress.SetUnlockedCount(campaign != null ? campaign.Count : 5);
            Debug.Log("[SleepDefenders] Todas as noites liberadas.");
        }

        [MenuItem("Sleep Defenders/Progresso/Zerar Progresso", priority = 21)]
        public static void ResetNightProgress()
        {
            LevelProgress.ResetProgress();
            Debug.Log("[SleepDefenders] Progresso zerado: só a Noite 1 está liberada.");
        }

        /// <summary>Entrada para linha de comando: Unity -batchmode -executeMethod SleepDefenders.EditorTools.SleepDefendersBuilder.BuildBatch -quit</summary>
        public static void BuildBatch() => BuildAll();

        public static void BuildAll()
        {
            missingModels.Clear();
            try
            {
                Progress("Tags e layers", 0.05f);
                EnsureTagsAndLayers();
                GameLayers.ConfigureCollisionMatrix();

                Progress("TextMesh Pro", 0.1f);
                EnsureTMPEssentials();

                // A cena nova é criada ANTES dos assets: NewScene descarrega assets que nada referencia,
                // o que invalidaria prefabs, cards e a campanha criados logo antes.
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                Progress("Pastas e texturas", 0.15f);
                EnsureFolders();
                ConfigureArtTextures();
                var sprites = CreateGeneratedSprites();

                Progress("Materiais", 0.25f);
                var mats = CreateMaterials(sprites);

                Progress("Efeitos", 0.35f);
                var vfx = CreateVfxPrefabs(mats);
                LightOrb orb = CreateLightOrbPrefab(mats);
                GameObject beam = CreateMoonbeamPrefab(mats);
                LaserProjectile laser = CreateProjectilePrefab(mats);

                Progress("Brinquedos", 0.5f);
                var lamp = CreateLampPrefab(orb, mats);
                var robot = CreateRobotPrefab(laser);
                var bear = CreateBearPrefab();
                var car = CreateCarPrefab();

                Progress("Monstros", 0.65f);
                var creeper = CreateCreeperPrefab();
                var closet = CreateClosetPrefab();
                var flyer = CreateFlyerPrefab();

                CreateLaneGuardPrefab(); // a cena carrega o prefab pelo caminho (ver BuildScene)

                Progress("Cards", 0.72f);
                var defs = new List<ToyDefinition>
                {
                    CreateDefinition("Toy_Abajur", lamp, "toy_lamp", 7.5f, false, "Gera 25 de Luz a cada 10 segundos."),
                    CreateDefinition("Toy_RoboLaser", robot, "laser_robot", 7.5f, false, "Dispara lasers nos monstros da sua faixa."),
                    CreateDefinition("Toy_Urso", bear, "teddy_bear", 25f, false, "Muita vida. Segura os monstros."),
                    CreateDefinition("Toy_CarrinhoRC", car, "rc_car", 30f, false, "Uso único: atropela todos os monstros da faixa."),
                };

                Progress("Campanha (5 noites)", 0.76f);
                Campaign campaign = CreateCampaign(defs, creeper, closet, flyer);

                Progress("Cena", 0.8f);
                BuildScene(scene, mats, sprites, vfx, orb, beam, defs, campaign);

                AssetDatabase.SaveAssets();
                Debug.Log("[SleepDefenders] Jogo construído com sucesso: " + ScenePath +
                          (missingModels.Count > 0 ? " | modelos ausentes: " + string.Join(", ", missingModels) : ""));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Progress(string info, float t)
        {
            if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("Sleep Defenders", info, t);
        }

        // ======================================================================= TAGS / LAYERS / TMP

        private static void EnsureTagsAndLayers()
        {
            Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(tagManager);

            SerializedProperty tags = so.FindProperty("tags");
            foreach (string tag in new[] { GameTags.Enemy, GameTags.Defender, GameTags.Projectile, GameTags.LightOrb })
            {
                bool exists = false;
                for (int i = 0; i < tags.arraySize; i++)
                    if (tags.GetArrayElementAtIndex(i).stringValue == tag) exists = true;
                if (exists) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }

            SerializedProperty layers = so.FindProperty("layers");
            foreach (string layer in new[] { GameLayers.GridName, GameLayers.EnemyName, GameLayers.DefenderName, GameLayers.ProjectileName, GameLayers.PickupName })
            {
                bool exists = false;
                for (int i = 0; i < layers.arraySize; i++)
                    if (layers.GetArrayElementAtIndex(i).stringValue == layer) exists = true;
                if (exists) continue;
                for (int i = 8; i < layers.arraySize; i++)
                {
                    SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                    slot.stringValue = layer;
                    break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureTMPEssentials()
        {
            const string fontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (font == null)
            {
                // AssetDatabase.ImportPackage é assíncrono; extraímos o .unitypackage na hora para
                // poder usar a fonte ainda nesta mesma construção.
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui/package.json");
                if (info != null)
                {
                    string pkg = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                    if (File.Exists(pkg))
                    {
                        UnityPackageExtractor.Extract(pkg);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    }
                }
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            }
            if (font == null && TMP_Settings.instance != null) font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                Debug.LogWarning("[SleepDefenders] Fonte TMP não encontrada. Importe 'Window > TextMeshPro > Import TMP Essential Resources' e construa de novo.");
                return;
            }

            // Material com contorno + sombra para os textos ficarem legíveis sobre o cenário.
            string matPath = Gen + "/Materials/Font_Outline.mat";
            EnsureFolder(Gen + "/Materials");
            fontOutlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (fontOutlineMaterial == null)
            {
                fontOutlineMaterial = new Material(font.material) { name = "Font_Outline" };
                AssetDatabase.CreateAsset(fontOutlineMaterial, matPath);
            }
            fontOutlineMaterial.CopyPropertiesFromMaterial(font.material);
            fontOutlineMaterial.shader = font.material.shader;
            fontOutlineMaterial.EnableKeyword("OUTLINE_ON");
            fontOutlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            fontOutlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.08f, 0.05f, 0.18f, 1f));
            fontOutlineMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
            fontOutlineMaterial.EnableKeyword("UNDERLAY_ON");
            fontOutlineMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
            fontOutlineMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            fontOutlineMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            fontOutlineMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
            EditorUtility.SetDirty(fontOutlineMaterial);
        }

        // ======================================================================= PASTAS E TEXTURAS

        private static void EnsureFolders()
        {
            foreach (string f in new[] { Gen, Gen + "/Materials", Gen + "/Prefabs", Gen + "/Data", Gen + "/Textures", Root + "/Scenes" })
                EnsureFolder(f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void ConfigureArtTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesDir }))
                ConfigureTexture(AssetDatabase.GUIDToAssetPath(guid), false, 1024, TextureWrapMode.Repeat);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconsDir }))
                ConfigureTexture(AssetDatabase.GUIDToAssetPath(guid), true, 512, TextureWrapMode.Clamp);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UIArtDir }))
                ConfigureTexture(AssetDatabase.GUIDToAssetPath(guid), true, 2048, TextureWrapMode.Clamp);
        }

        private static void ConfigureTexture(string path, bool sprite, int maxSize, TextureWrapMode wrap, Vector4 border = default)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
            bool changed = false;
            void Set<T>(T current, T value, Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, value)) return;
                apply(value);
                changed = true;
            }

            Set(imp.textureType, sprite ? TextureImporterType.Sprite : TextureImporterType.Default, v => imp.textureType = v);
            if (sprite)
            {
                Set(imp.spriteImportMode, SpriteImportMode.Single, v => imp.spriteImportMode = v);
                Set(imp.alphaIsTransparency, true, v => imp.alphaIsTransparency = v);
                Set(imp.mipmapEnabled, false, v => imp.mipmapEnabled = v);
                Set(imp.spriteBorder, border, v => imp.spriteBorder = v);
            }
            Set(imp.wrapMode, wrap, v => imp.wrapMode = v);
            Set(imp.maxTextureSize, maxSize, v => imp.maxTextureSize = v);
            if (changed) imp.SaveAndReimport();
        }

        // ======================================================================= SPRITES GERADOS

        private class Sprites
        {
            public Sprite rounded, roundedOutline, circle, glow;
            public Texture2D glowTexture;
        }

        private static Sprites CreateGeneratedSprites()
        {
            var s = new Sprites();
            s.rounded = MakeSprite("ui_rounded", 64, (x, y) => RoundedRect(x, y, 64, 18f, 0f), new Vector4(22, 22, 22, 22));
            s.roundedOutline = MakeSprite("ui_rounded_outline", 64, (x, y) => RoundedRect(x, y, 64, 18f, 5f), new Vector4(22, 22, 22, 22));
            s.circle = MakeSprite("ui_circle", 128, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64));
                return Mathf.Clamp01(62f - d);
            }, Vector4.zero);
            s.glow = MakeSprite("ui_glow", 128, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                float a = Mathf.Clamp01(1f - d);
                return a * a;
            }, Vector4.zero);
            s.glowTexture = s.glow.texture;
            return s;
        }

        private static float RoundedRect(int x, int y, int size, float radius, float outline)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            Vector2 half = new Vector2(size * 0.5f, size * 0.5f);
            Vector2 q = new Vector2(Mathf.Abs(p.x - half.x), Mathf.Abs(p.y - half.y)) - (half - new Vector2(radius + 1f, radius + 1f));
            float dist = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            float inside = Mathf.Clamp01(0.5f - dist);
            if (outline <= 0f) return inside;
            float inner = Mathf.Clamp01(0.5f - (dist + outline));
            return inside - inner;
        }

        private static Sprite MakeSprite(string name, int size, Func<int, int, float> alpha, Vector4 border)
        {
            string path = $"{Gen}/Textures/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(path, true, 256, TextureWrapMode.Clamp, border);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ======================================================================= MATERIAIS

        private class Mats
        {
            public Material floor, wall, rugA, rugB, rugBorder, highlight, laser, laserTrail, orb, orbGlow, beam,
                puff, additive, moon, windowGlass, wood, placeholder;
        }

        private static Mats CreateMaterials(Sprites sprites)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Mats();

            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{n}.png");

            m.floor = Mat("Floor", lit, x =>
            {
                x.SetTexture("_BaseMap", Tex("floor_wood"));
                x.SetTextureScale("_BaseMap", new Vector2(10f, 8f));
                x.SetColor("_BaseColor", new Color(0.85f, 0.75f, 0.68f));
                x.SetFloat("_Smoothness", 0.35f);
            });
            m.wall = Mat("Wallpaper", lit, x =>
            {
                x.SetTexture("_BaseMap", Tex("wallpaper_night"));
                x.SetTextureScale("_BaseMap", new Vector2(9f, 2.5f));
                x.SetColor("_BaseColor", Color.white);
                x.SetFloat("_Smoothness", 0.1f);
            });
            m.rugA = Mat("Rug_A", lit, x =>
            {
                x.SetTexture("_BaseMap", Tex("rug_wool"));
                x.SetColor("_BaseColor", new Color(1f, 0.95f, 0.86f));
                x.SetFloat("_Smoothness", 0.05f);
            });
            m.rugB = Mat("Rug_B", lit, x =>
            {
                x.SetTexture("_BaseMap", Tex("rug_wool"));
                x.SetColor("_BaseColor", new Color(0.72f, 0.76f, 0.98f));
                x.SetFloat("_Smoothness", 0.05f);
            });
            m.rugBorder = Mat("Rug_Border", lit, x =>
            {
                x.SetTexture("_BaseMap", Tex("rug_wool"));
                x.SetTextureScale("_BaseMap", new Vector2(6f, 4f));
                x.SetColor("_BaseColor", new Color(0.36f, 0.24f, 0.55f));
                x.SetFloat("_Smoothness", 0.05f);
            });
            m.highlight = Mat("Cell_Highlight", unlit, x =>
            {
                x.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.45f));
                MakeTransparent(x, false);
            });
            m.laser = Mat("Laser", unlit, x => x.SetColor("_BaseColor", new Color(0.6f, 3.2f, 3.6f)));
            m.laserTrail = Mat("Laser_Trail", particles, x =>
            {
                x.SetTexture("_BaseMap", sprites.glowTexture);
                x.SetColor("_BaseColor", new Color(0.4f, 2.2f, 2.6f, 1f));
                MakeTransparent(x, true);
            });
            m.orb = Mat("LightOrb", unlit, x => x.SetColor("_BaseColor", new Color(2.1f, 1.6f, 0.55f)));
            m.orbGlow = Mat("LightOrb_Glow", particles, x =>
            {
                x.SetTexture("_BaseMap", sprites.glowTexture);
                x.SetColor("_BaseColor", new Color(1f, 0.72f, 0.25f, 0.55f));
                MakeTransparent(x, true);
            });
            m.beam = Mat("Moonbeam", unlit, x =>
            {
                x.SetColor("_BaseColor", new Color(0.45f, 0.55f, 0.9f, 0.18f));
                MakeTransparent(x, true);
            });
            m.puff = Mat("Particle_Puff", particles, x =>
            {
                x.SetTexture("_BaseMap", sprites.glowTexture);
                x.SetColor("_BaseColor", Color.white);
                MakeTransparent(x, false);
            });
            m.additive = Mat("Particle_Additive", particles, x =>
            {
                x.SetTexture("_BaseMap", sprites.glowTexture);
                x.SetColor("_BaseColor", new Color(1.5f, 1.5f, 1.5f, 1f));
                MakeTransparent(x, true);
            });
            m.moon = Mat("Moon", unlit, x => x.SetColor("_BaseColor", new Color(2.4f, 2.4f, 2.1f)));
            m.windowGlass = Mat("WindowSky", unlit, x => x.SetColor("_BaseColor", new Color(0.08f, 0.12f, 0.32f)));
            m.wood = Mat("WindowFrame", lit, x =>
            {
                x.SetColor("_BaseColor", new Color(0.93f, 0.9f, 0.98f));
                x.SetFloat("_Smoothness", 0.3f);
            });
            m.placeholder = Mat("Placeholder", lit, x => x.SetColor("_BaseColor", Color.white));
            return m;
        }

        private static Material Mat(string name, Shader shader, Action<Material> setup)
        {
            string path = $"{Gen}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material ColorMat(string name, Color color, float smoothness = 0.4f, Color? emission = null)
        {
            return Mat("Color_" + name, Shader.Find("Universal Render Pipeline/Lit"), x =>
            {
                x.SetColor("_BaseColor", color);
                x.SetFloat("_Smoothness", smoothness);
                if (emission.HasValue)
                {
                    x.EnableKeyword("_EMISSION");
                    x.SetColor("_EmissionColor", emission.Value);
                    x.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            });
        }

        /// <summary>Configura um material URP (Lit/Unlit/Particles) como transparente (alpha) ou aditivo.</summary>
        private static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        // ======================================================================= MODELOS

        /// <summary>
        /// Cria o filho "Visual" com o modelo do Tripo (ou um placeholder), normalizado:
        /// altura = targetHeight, pegada máxima = maxFootprint, base no chão (y = 0), centralizado em X/Z.
        /// </summary>
        private static Transform BuildVisual(GameObject root, string modelId, float targetHeight, float maxFootprint, float yaw, Func<GameObject> placeholder)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/{modelId}.glb");
            GameObject model;
            if (asset != null)
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            }
            else
            {
                if (!missingModels.Contains(modelId)) missingModels.Add(modelId);
                model = placeholder != null ? placeholder() : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }

            model.name = "Model";
            model.transform.SetParent(visual, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one;
            FitModel(model.transform, targetHeight, maxFootprint);

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return visual;
        }

        private static void FitModel(Transform model, float targetHeight, float maxFootprint)
        {
            Bounds b = MeshBounds(model);
            if (b.size.y <= 0.0001f) return;
            float s = targetHeight / b.size.y;
            if (maxFootprint > 0f) s = Mathf.Min(s, maxFootprint / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z)));
            model.localScale *= s;
            b = MeshBounds(model);
            Transform parent = model.parent;
            Vector3 localMin = parent != null ? parent.InverseTransformPoint(b.min) : b.min;
            Vector3 localCenter = parent != null ? parent.InverseTransformPoint(b.center) : b.center;
            model.localPosition += new Vector3(-localCenter.x, -localMin.y, -localCenter.z);
        }

        /// <summary>Bounds em espaço de mundo calculados a partir das malhas (não depende de um frame renderizado).</summary>
        public static Bounds MeshBounds(Transform t)
        {
            bool has = false;
            var bounds = new Bounds(t.position, Vector3.zero);
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(w, Vector3.zero); has = true; }
                    else bounds.Encapsulate(w);
                }
            }
            foreach (SkinnedMeshRenderer smr in t.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!has) { bounds = smr.bounds; has = true; }
                else bounds.Encapsulate(smr.bounds);
            }
            return bounds;
        }

        private static GameObject Placeholder(params (PrimitiveType type, Vector3 pos, Vector3 scale, Color color)[] parts)
        {
            var root = new GameObject("Placeholder");
            int i = 0;
            foreach (var p in parts)
            {
                GameObject g = GameObject.CreatePrimitive(p.type);
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = p.pos;
                g.transform.localScale = p.scale;
                g.GetComponent<Renderer>().sharedMaterial = ColorMat($"PH_{ColorUtility.ToHtmlStringRGB(p.color)}", p.color);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                i++;
            }
            return root;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (layer < 0) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        // ======================================================================= PREFABS: EFEITOS

        private class VfxPrefabs { public ParticleSystem puff, spark, sparkle; }

        private static VfxPrefabs CreateVfxPrefabs(Mats mats)
        {
            return new VfxPrefabs
            {
                puff = MakeParticles("VFX_Puff", mats.puff, 14, new Vector2(0.45f, 0.8f), new Vector2(0.6f, 1.6f), new Vector2(0.25f, 0.5f), -0.15f, 0.25f, true),
                spark = MakeParticles("VFX_Spark", mats.additive, 10, new Vector2(0.15f, 0.3f), new Vector2(2f, 4f), new Vector2(0.06f, 0.12f), 0.3f, 0.05f, false),
                sparkle = MakeParticles("VFX_Sparkle", mats.additive, 14, new Vector2(0.35f, 0.6f), new Vector2(0.6f, 1.6f), new Vector2(0.08f, 0.16f), -0.3f, 0.15f, false),
            };
        }

        private static ParticleSystem MakeParticles(string name, Material mat, int burst, Vector2 life, Vector2 speed, Vector2 size, float gravity, float radius, bool grow)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = Color.white;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = burst * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, grow ? AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f) : AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat;
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off;

            return SavePrefab(go, name).GetComponent<ParticleSystem>();
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            string path = $"{Gen}/Prefabs/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static LightOrb CreateLightOrbPrefab(Mats mats)
        {
            var root = new GameObject("LightOrb");
            root.tag = GameTags.LightOrb;
            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.45f; // generoso para facilitar o clique

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.name = "Core";
            core.transform.SetParent(visual, false);
            core.transform.localScale = Vector3.one * 0.28f;
            core.GetComponent<Renderer>().sharedMaterial = mats.orb;
            core.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.name = "Halo";
            halo.transform.SetParent(visual, false);
            halo.transform.localScale = Vector3.one * 1.1f;
            halo.GetComponent<Renderer>().sharedMaterial = mats.orbGlow;
            halo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            halo.AddComponent<Billboard>();

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.85f, 0.45f);
            light.range = 1.4f;
            light.intensity = 0.8f;
            light.shadows = LightShadows.None;

            var orb = root.AddComponent<LightOrb>();
            SetFields(orb, ("visual", visual), ("orbLight", light));
            SetLayerRecursively(root, GameLayers.Pickup);
            return SavePrefab(root, "LightOrb").GetComponent<LightOrb>();
        }

        private static GameObject CreateMoonbeamPrefab(Mats mats)
        {
            var root = new GameObject("Moonbeam");
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.transform.SetParent(root.transform, false);
            cyl.transform.localPosition = new Vector3(0f, 4f, 0f);
            cyl.transform.localScale = new Vector3(0.75f, 4f, 0.75f);
            var r = cyl.GetComponent<Renderer>();
            r.sharedMaterial = mats.beam;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return SavePrefab(root, "Moonbeam");
        }

        private static LaserProjectile CreateProjectilePrefab(Mats mats)
        {
            var root = new GameObject("LaserProjectile");
            root.tag = GameTags.Projectile;
            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.15f;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(bolt.GetComponent<Collider>());
            bolt.name = "Bolt";
            bolt.transform.SetParent(root.transform, false);
            bolt.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            bolt.transform.localScale = new Vector3(0.09f, 0.2f, 0.09f);
            var r = bolt.GetComponent<Renderer>();
            r.sharedMaterial = mats.laser;
            r.shadowCastingMode = ShadowCastingMode.Off;

            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.startWidth = 0.1f;
            trail.endWidth = 0f;
            trail.sharedMaterial = mats.laserTrail;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.minVertexDistance = 0.05f;

            root.AddComponent<LaserProjectile>();
            SetLayerRecursively(root, GameLayers.Projectile);
            return SavePrefab(root, "LaserProjectile").GetComponent<LaserProjectile>();
        }

        // ======================================================================= PREFABS: BRINQUEDOS

        private static GameObject NewToyRoot(string name, Vector3 colliderSize, bool trigger)
        {
            var root = new GameObject(name) { tag = GameTags.Defender };
            var box = root.AddComponent<BoxCollider>();
            box.size = colliderSize;
            box.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            box.isTrigger = trigger;
            return root;
        }

        private static T FinishToy<T>(GameObject root, Transform visual, string prefabName) where T : ToyBase
        {
            var anim = root.AddComponent<ProceduralAnimator>();
            anim.Configure(visual, false);
            root.AddComponent<HitFlash>();
            SetLayerRecursively(root, GameLayers.Defender);
            return root.GetComponent<T>();
        }

        private static ToyLamp CreateLampPrefab(LightOrb orb, Mats mats)
        {
            GameObject root = NewToyRoot("Toy_Abajur", new Vector3(0.7f, 0.85f, 0.7f), false);
            Transform visual = BuildVisual(root, "toy_lamp", 0.78f, 0.78f, ToyYaw, () => Placeholder(
                (PrimitiveType.Cylinder, new Vector3(0, 0.25f, 0), new Vector3(0.45f, 0.25f, 0.45f), new Color(1f, 0.9f, 0.5f)),
                (PrimitiveType.Sphere, new Vector3(0, 0.7f, 0), new Vector3(0.7f, 0.45f, 0.7f), new Color(1f, 0.97f, 0.8f))));

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.42f);
            light.range = 2.4f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;

            var lamp = root.AddComponent<ToyLamp>();
            SetFields(lamp, ("displayName", "Abajur"), ("cost", 50), ("maxHP", 80f), ("lightPerCycle", 25), ("interval", 10f),
                ("orbPrefab", orb), ("glowLight", light));
            FinishToy<ToyLamp>(root, visual, "Toy_Abajur");
            return SavePrefab(root, "Toy_Abajur").GetComponent<ToyLamp>();
        }

        private static LaserRobot CreateRobotPrefab(LaserProjectile laser)
        {
            GameObject root = NewToyRoot("Toy_RoboLaser", new Vector3(0.7f, 0.95f, 0.7f), true);
            Transform visual = BuildVisual(root, "laser_robot", 0.95f, 1.05f, ToyYaw, () => Placeholder(
                (PrimitiveType.Cube, new Vector3(0, 0.35f, 0), new Vector3(0.45f, 0.45f, 0.35f), new Color(0.9f, 0.15f, 0.15f)),
                (PrimitiveType.Cube, new Vector3(0, 0.75f, 0), new Vector3(0.35f, 0.3f, 0.3f), new Color(0.8f, 0.8f, 0.85f)),
                (PrimitiveType.Cylinder, new Vector3(-0.3f, 0.45f, 0), new Vector3(0.12f, 0.2f, 0.12f), new Color(0.3f, 0.9f, 1f))));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0.42f, 0.5f, 0f);

            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(muzzle, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 1f, 1f);
            light.range = 1.8f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            var robot = root.AddComponent<LaserRobot>();
            SetFields(robot, ("displayName", "Robô Laser"), ("cost", 100), ("maxHP", 100f), ("fireInterval", 1.4f), ("damage", 20f),
                ("projectilePrefab", laser), ("muzzle", muzzle), ("muzzleLight", light), ("rayHeight", 0.45f));
            FinishToy<LaserRobot>(root, visual, "Toy_RoboLaser");
            return SavePrefab(root, "Toy_RoboLaser").GetComponent<LaserRobot>();
        }

        private static TeddyBear CreateBearPrefab()
        {
            GameObject root = NewToyRoot("Toy_Urso", new Vector3(0.85f, 0.95f, 0.85f), false);
            Transform visual = BuildVisual(root, "teddy_bear", 0.95f, 0.95f, ToyYaw, () => Placeholder(
                (PrimitiveType.Sphere, new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.7f, 0.6f), new Color(0.6f, 0.38f, 0.2f)),
                (PrimitiveType.Sphere, new Vector3(0, 0.82f, 0), new Vector3(0.45f, 0.42f, 0.42f), new Color(0.65f, 0.42f, 0.22f))));

            var bear = root.AddComponent<TeddyBear>();
            SetFields(bear, ("displayName", "Urso de Pelúcia"), ("cost", 50), ("maxHP", 400f), ("visualToSquash", visual.Find("Model")));
            FinishToy<TeddyBear>(root, visual, "Toy_Urso");
            return SavePrefab(root, "Toy_Urso").GetComponent<TeddyBear>();
        }

        private static RCCar CreateCarPrefab()
        {
            GameObject root = NewToyRoot("Toy_CarrinhoRC", new Vector3(0.85f, 0.55f, 0.7f), true);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            Transform visual = BuildVisual(root, "rc_car", 0.6f, 0.9f, 190f, () => Placeholder(
                (PrimitiveType.Cube, new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.25f, 0.45f), new Color(1f, 0.55f, 0.1f)),
                (PrimitiveType.Cylinder, new Vector3(0.25f, 0.15f, 0.25f), new Vector3(0.25f, 0.05f, 0.25f), Color.black),
                (PrimitiveType.Cylinder, new Vector3(-0.25f, 0.15f, 0.25f), new Vector3(0.25f, 0.05f, 0.25f), Color.black),
                (PrimitiveType.Cylinder, new Vector3(0.25f, 0.15f, -0.25f), new Vector3(0.25f, 0.05f, 0.25f), Color.black),
                (PrimitiveType.Cylinder, new Vector3(-0.25f, 0.15f, -0.25f), new Vector3(0.25f, 0.05f, 0.25f), Color.black)));

            var car = root.AddComponent<RCCar>();
            SetFields(car, ("displayName", "Carrinho RC"), ("cost", 175), ("maxHP", 1f), ("blocksEnemies", false), ("wheelieTarget", visual.Find("Model")));
            FinishToy<RCCar>(root, visual, "Toy_CarrinhoRC");
            return SavePrefab(root, "Toy_CarrinhoRC").GetComponent<RCCar>();
        }

        // ======================================================================= PREFABS: MONSTROS

        private static GameObject NewEnemyRoot(string name, float radius, float height)
        {
            var root = new GameObject(name) { tag = GameTags.Enemy };
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return root;
        }

        private static void FinishEnemy(GameObject root, Transform visual, bool flying)
        {
            var anim = root.AddComponent<ProceduralAnimator>();
            anim.Configure(visual, flying);
            root.AddComponent<HitFlash>();
            SetLayerRecursively(root, GameLayers.Enemy);
        }

        private static ShadowCreeper CreateCreeperPrefab()
        {
            GameObject root = NewEnemyRoot("Enemy_SombraRastejante", 0.32f, 1f);
            Transform visual = BuildVisual(root, "shadow_creeper", 0.9f, 0.95f, EnemyYaw, () => Placeholder(
                (PrimitiveType.Capsule, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.45f, 0.5f), new Color(0.15f, 0.1f, 0.3f)),
                (PrimitiveType.Sphere, new Vector3(-0.25f, 0.65f, 0.12f), Vector3.one * 0.15f, new Color(1f, 0.9f, 0.2f)),
                (PrimitiveType.Sphere, new Vector3(-0.25f, 0.65f, -0.12f), Vector3.one * 0.15f, new Color(1f, 0.9f, 0.2f))));
            FinishEnemy(root, visual, false);
            var e = root.AddComponent<ShadowCreeper>();
            // Velocidades: 1 unidade = 1 célula. A Sombra cruza o tapete (~10,6 unidades) em ~33 s.
            SetFields(e, ("displayName", "Sombra Rastejante"), ("maxHP", 100f), ("moveSpeed", 0.32f), ("attackDamage", 20f), ("attackInterval", 1f));
            return SavePrefab(root, "Enemy_SombraRastejante").GetComponent<ShadowCreeper>();
        }

        private static ClosetMonster CreateClosetPrefab()
        {
            GameObject root = NewEnemyRoot("Enemy_MonstroDoArmario", 0.4f, 1.4f);
            Transform visual = BuildVisual(root, "closet_monster", 1.4f, 1.1f, EnemyYaw, () => Placeholder(
                (PrimitiveType.Capsule, new Vector3(0, 0.7f, 0), new Vector3(0.9f, 0.7f, 0.8f), new Color(0.25f, 0.2f, 0.5f)),
                (PrimitiveType.Sphere, new Vector3(-0.38f, 1f, 0), Vector3.one * 0.3f, new Color(1f, 0.9f, 0.2f))));
            FinishEnemy(root, visual, false);
            var e = root.AddComponent<ClosetMonster>();
            SetFields(e, ("displayName", "Monstro do Armário"), ("maxHP", 450f), ("moveSpeed", 0.2f), ("attackDamage", 35f), ("attackInterval", 1.2f),
                ("deathPuffColor", new Color(0.3f, 0.25f, 0.6f, 0.9f)));
            return SavePrefab(root, "Enemy_MonstroDoArmario").GetComponent<ClosetMonster>();
        }

        private static WingedNightmare CreateFlyerPrefab()
        {
            GameObject root = NewEnemyRoot("Enemy_PesadeloAlado", 0.3f, 1f);
            Transform visual = BuildVisual(root, "winged_nightmare", 0.7f, 1f, EnemyYaw, () => Placeholder(
                (PrimitiveType.Sphere, new Vector3(0, 0.35f, 0), Vector3.one * 0.45f, new Color(0.3f, 0.12f, 0.4f)),
                (PrimitiveType.Cube, new Vector3(0, 0.45f, 0.4f), new Vector3(0.3f, 0.05f, 0.5f), new Color(0.25f, 0.1f, 0.35f)),
                (PrimitiveType.Cube, new Vector3(0, 0.45f, -0.4f), new Vector3(0.3f, 0.05f, 0.5f), new Color(0.25f, 0.1f, 0.35f))));
            FinishEnemy(root, visual, true);
            var e = root.AddComponent<WingedNightmare>();
            SetFields(e, ("displayName", "Pesadelo Alado"), ("maxHP", 70f), ("moveSpeed", 0.8f), ("landedSpeed", 0.4f), ("attackDamage", 15f),
                ("attackInterval", 0.8f), ("deathPuffColor", new Color(0.55f, 0.2f, 0.6f, 0.9f)));
            return SavePrefab(root, "Enemy_PesadeloAlado").GetComponent<WingedNightmare>();
        }

        // ======================================================================= ÚLTIMA DEFESA

        private static void CreateLaneGuardPrefab()
        {
            var root = new GameObject("LaneGuard_RatinhoDeCorda");
            // Placeholder com a "frente" em -X, como os modelos do Tripo (o yaw vira para +X).
            Transform visual = BuildVisual(root, "windup_mouse", 0.46f, 0.62f, ToyYaw, () => Placeholder(
                (PrimitiveType.Sphere, new Vector3(0, 0.18f, 0), new Vector3(0.5f, 0.3f, 0.34f), new Color(0.62f, 0.7f, 0.85f)),
                (PrimitiveType.Sphere, new Vector3(-0.16f, 0.32f, 0.1f), Vector3.one * 0.13f, new Color(1f, 0.62f, 0.75f)),
                (PrimitiveType.Sphere, new Vector3(-0.16f, 0.32f, -0.1f), Vector3.one * 0.13f, new Color(1f, 0.62f, 0.75f)),
                (PrimitiveType.Cylinder, new Vector3(0.08f, 0.38f, 0), new Vector3(0.05f, 0.06f, 0.05f), new Color(1f, 0.8f, 0.25f))));
            var anim = root.AddComponent<ProceduralAnimator>();
            anim.Configure(visual, false);
            root.AddComponent<LaneGuard>();
            SavePrefab(root, "LaneGuard_RatinhoDeCorda");
        }

        // ======================================================================= CAMPANHA (5 NOITES)

        /// <summary>
        /// As 5 noites do jogo, com dificuldade crescente. Cada noite libera um brinquedo ou monstro novo:
        ///   Noite 1  Abajur + Robô, só Sombras, só nas 3 faixas do meio (aprendizado)
        ///   Noite 2  + Urso de Pelúcia, chegam os Pesadelos Alados
        ///   Noite 3  + Carrinho RC, chega o Monstro do Armário
        ///   Noite 4  todos os brinquedos e monstros, com uma onda grande no meio
        ///   Noite 5  a noite mais longa, com duas ondas grandes
        /// </summary>
        private static Campaign CreateCampaign(List<ToyDefinition> defs, EnemyBase creeper, EnemyBase closet, EnemyBase flyer)
        {
            var campaign = AssetDatabase.LoadAssetAtPath<Campaign>(CampaignPath);
            if (campaign == null)
            {
                campaign = ScriptableObject.CreateInstance<Campaign>();
                AssetDatabase.CreateAsset(campaign, CampaignPath);
            }

            ToyDefinition lamp = defs[0], robot = defs[1], bear = defs[2], car = defs[3];
            int[] middle = { 1, 2, 3 };

            Wave W(string name, float interval, float delay, bool big, int[] lanes, params (EnemyBase prefab, int count)[] entries)
            {
                var w = new Wave { name = name, spawnInterval = interval, delayBeforeWave = delay, isBigWave = big };
                if (lanes != null) w.lanes.AddRange(lanes);
                foreach (var e in entries) w.enemies.Add(new EnemySpawnEntry { prefab = e.prefab, count = e.count });
                return w;
            }

            NightDefinition N(string title, string subtitle, string tip, float initialDelay, ToyDefinition[] toys, params Wave[] waves)
            {
                var n = new NightDefinition { title = title, subtitle = subtitle, tip = tip, startingLight = 150, initialDelay = initialDelay, moonlight = true };
                n.toys.AddRange(toys);
                n.waves.AddRange(waves);
                return n;
            }

            campaign.nights = new List<NightDefinition>
            {
                N("Noite 1", "A Primeira Noite",
                    "Coloque Abajures para juntar Luz e Robôs Laser para proteger as 3 faixas do meio.",
                    30f, new[] { lamp, robot },
                    W("Onda 1", 14f, 0f, false, new[] { 2 }, (creeper, 2)),
                    W("Onda 2", 10f, 18f, false, middle, (creeper, 3)),
                    W("Onda 3", 7f, 18f, false, middle, (creeper, 5)),
                    W("Onda Final", 4f, 20f, true, middle, (creeper, 8))),

                N("Noite 2", "Asas no Escuro",
                    "Pesadelos Alados voam por cima do primeiro brinquedo da faixa. Use Ursos de Pelúcia para segurá-los!",
                    25f, new[] { lamp, robot, bear },
                    W("Onda 1", 11f, 0f, false, null, (creeper, 3)),
                    W("Onda 2", 9f, 18f, false, null, (creeper, 4), (flyer, 1)),
                    W("Onda 3", 7f, 18f, false, null, (creeper, 6), (flyer, 2)),
                    W("Onda 4", 6f, 18f, false, null, (creeper, 7), (flyer, 3)),
                    W("Onda Final", 3.5f, 20f, true, null, (creeper, 10), (flyer, 4))),

                N("Noite 3", "O Armário Rangeu",
                    "O Monstro do Armário é lento, mas aguenta muito dano. Guarde um Carrinho RC para emergências!",
                    25f, new[] { lamp, robot, bear, car },
                    W("Onda 1", 10f, 0f, false, null, (creeper, 4)),
                    W("Onda 2", 8f, 18f, false, null, (creeper, 5), (flyer, 2)),
                    W("Onda 3", 7f, 18f, false, null, (creeper, 6), (flyer, 2), (closet, 1)),
                    W("Onda 4", 6f, 18f, false, null, (creeper, 7), (flyer, 3), (closet, 2)),
                    W("Onda Final", 3.2f, 20f, true, null, (creeper, 11), (flyer, 4), (closet, 3))),

                N("Noite 4", "Tempestade de Sombras",
                    "Uma onda enorme chega no meio da noite. Monte uma defesa forte em todas as faixas!",
                    25f, new[] { lamp, robot, bear, car },
                    W("Onda 1", 9f, 0f, false, null, (creeper, 5)),
                    W("Onda 2", 7.5f, 16f, false, null, (creeper, 6), (flyer, 2)),
                    W("Onda 3", 6.5f, 16f, false, null, (creeper, 7), (flyer, 3), (closet, 1)),
                    W("Onda Grande", 3.5f, 18f, true, null, (creeper, 8), (flyer, 3), (closet, 2)),
                    W("Onda 5", 5f, 16f, false, null, (creeper, 9), (flyer, 4), (closet, 2)),
                    W("Onda Final", 2.8f, 20f, true, null, (creeper, 13), (flyer, 5), (closet, 4))),

                N("Noite 5", "A Noite Mais Longa",
                    "A última e mais longa noite. Aguente até o amanhecer!",
                    25f, new[] { lamp, robot, bear, car },
                    W("Onda 1", 8f, 0f, false, null, (creeper, 6)),
                    W("Onda 2", 7f, 15f, false, null, (creeper, 7), (flyer, 3)),
                    W("Onda 3", 6f, 15f, false, null, (creeper, 8), (flyer, 3), (closet, 2)),
                    W("Onda Grande", 3.2f, 18f, true, null, (creeper, 10), (flyer, 4), (closet, 2)),
                    W("Onda 5", 5f, 15f, false, null, (creeper, 10), (flyer, 5), (closet, 3)),
                    W("Onda 6", 4.5f, 15f, false, null, (creeper, 11), (flyer, 5), (closet, 4)),
                    W("Onda Final", 2.4f, 20f, true, null, (creeper, 16), (flyer, 7), (closet, 5))),
            };

            EditorUtility.SetDirty(campaign);
            return campaign;
        }

        private static ToyDefinition CreateDefinition(string assetName, ToyBase prefab, string iconId, float recharge, bool startsRecharging, string description)
        {
            string path = $"{Gen}/Data/{assetName}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ToyDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ToyDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.prefab = prefab;
            def.icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsDir}/{iconId}.png");
            def.rechargeTime = recharge;
            def.startsRecharging = startsRecharging;
            def.description = description;
            EditorUtility.SetDirty(def);
            return def;
        }

        // ======================================================================= CENA

        private static void BuildScene(Scene scene, Mats mats, Sprites sprites, VfxPrefabs vfx, LightOrb orb, GameObject beam,
            List<ToyDefinition> defs, Campaign campaign)
        {
            // --- Iluminação de quarto à noite: luar azulado + ambiente suave + luzes quentes dos abajures.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.45f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.28f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.2f);
            RenderSettings.fog = false;

            // --- Câmera 3/4 olhando o tapete de frente (faixas na horizontal da tela).
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.08f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 80f;
            camGo.transform.position = new Vector3(-1f, 8f, -8.9f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.3f, 1f) - camGo.transform.position);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var moonGo = new GameObject("Luar (Directional Light)");
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.7f, 1f);
            moon.intensity = 1.35f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.75f;
            moonGo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);

            var warmGo = new GameObject("Luz Noturna (Point Light)");
            var warm = warmGo.AddComponent<Light>();
            warm.type = LightType.Point;
            warm.color = new Color(1f, 0.72f, 0.45f);
            warm.range = 9f;
            warm.intensity = 2.2f;
            warm.shadows = LightShadows.None;
            warmGo.transform.position = new Vector3(-6.5f, 3.2f, 1.5f);

            CreatePostProcessVolume();

            // --- Quarto
            var room = new GameObject("Quarto").transform;
            Box("Chao", room, new Vector3(0f, -0.06f, 2f), new Vector3(44f, 0.1f, 34f), mats.floor, false);
            Box("Parede_Fundo", room, new Vector3(0f, 5f, 6.5f), new Vector3(44f, 10f, 0.2f), mats.wall, false);
            Box("Parede_Esquerda", room, new Vector3(-13f, 5f, 0f), new Vector3(0.2f, 10f, 30f), mats.wall, false);
            Box("Parede_Direita", room, new Vector3(13f, 5f, 0f), new Vector3(0.2f, 10f, 30f), mats.wall, false);
            Box("Tapete_Borda", room, new Vector3(0f, -0.008f, 0f), new Vector3(9.8f, 0.01f, 5.8f), mats.rugBorder, false);

            // Janela com lua na parede do fundo (centro do topo da tela, entre os painéis da UI)
            const float wx = 1.6f, wy = 2.7f;
            Box("Janela_Ceu", room, new Vector3(wx, wy, 6.38f), new Vector3(2.8f, 2.0f, 0.02f), mats.windowGlass, false);
            Box("Janela_Moldura_Topo", room, new Vector3(wx, wy + 1.07f, 6.33f), new Vector3(3.1f, 0.15f, 0.12f), mats.wood, true);
            Box("Janela_Moldura_Base", room, new Vector3(wx, wy - 1.07f, 6.33f), new Vector3(3.1f, 0.15f, 0.12f), mats.wood, true);
            Box("Janela_Moldura_Esq", room, new Vector3(wx - 1.47f, wy, 6.33f), new Vector3(0.15f, 2.3f, 0.12f), mats.wood, true);
            Box("Janela_Moldura_Dir", room, new Vector3(wx + 1.47f, wy, 6.33f), new Vector3(0.15f, 2.3f, 0.12f), mats.wood, true);
            Box("Janela_Moldura_Meio", room, new Vector3(wx, wy, 6.33f), new Vector3(0.08f, 2.0f, 0.08f), mats.wood, true);
            GameObject moonDisc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(moonDisc.GetComponent<Collider>());
            moonDisc.name = "Lua";
            moonDisc.transform.SetParent(room, false);
            moonDisc.transform.position = new Vector3(wx + 0.65f, wy + 0.35f, 6.4f);
            moonDisc.transform.localScale = new Vector3(0.6f, 0.6f, 0.02f);
            moonDisc.GetComponent<Renderer>().sharedMaterial = mats.moon;

            // Cama (objetivo) à esquerda, com a cabeceira encostada na parede do fundo e o comprimento ao longo
            // das faixas; armário (de onde vêm os monstros) no canto do fundo à direita.
            // (A frente dos modelos do Tripo aponta para -X; yaw -90 vira a frente para -Z, ou seja, para a câmera.)
            Prop(room, "bed", new Vector3(-6.9f, 0f, 0.3f), -90f, 4.3f, 5.6f, false);
            Prop(room, "wardrobe", new Vector3(6.9f, 0f, 4.7f), -60f, 4.4f, 2.4f, false);
            Prop(room, "bookshelf", new Vector3(-2.6f, 0f, 5.85f), -90f, 2.6f, 2f, false);
            Prop(room, "toy_chest", new Vector3(6.3f, 0f, -3.6f), -53f, 1f, 1.4f, false);
            Prop(room, "toy_blocks", new Vector3(-4.3f, 0f, -3.5f), 20f, 0.5f, 0.8f, false);

            // --- Grade (tapete 5x9)
            var gridGo = new GameObject("Grid (Tapete 5x9)");
            var grid = gridGo.AddComponent<GridManager>();
            GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(highlight.GetComponent<Collider>());
            highlight.name = "Highlight";
            highlight.transform.SetParent(gridGo.transform, false);
            highlight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            highlight.transform.localScale = Vector3.one * 0.96f;
            var hr = highlight.GetComponent<Renderer>();
            hr.sharedMaterial = mats.highlight;
            hr.shadowCastingMode = ShadowCastingMode.Off;
            SetFields(grid, ("cellMaterialA", mats.rugA), ("cellMaterialB", mats.rugB), ("highlight", hr), ("raycastCamera", cam));
            grid.BuildGrid();

            // --- Última defesa: um Ratinho de Corda por faixa, na beira do tapete ao lado da cama.
            GameObject guardAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LaneGuardPrefabPath);
            var guards = new GameObject("Ratinhos de Corda (ultima defesa)").transform;
            for (int r = 0; r < grid.Rows; r++)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(guardAsset);
                g.name = $"RatinhoDeCorda_Faixa{r}";
                g.transform.SetParent(guards, true);
                g.transform.position = new Vector3(grid.LeftEdgeX - 0.22f, 0f, grid.GetLaneZ(r));
                SetFields(g.GetComponent<LaneGuard>(), ("lane", r));
            }

            // --- Gerenciadores
            var managers = new GameObject("Managers").transform;

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(managers, false);
            var game = gmGo.AddComponent<GameManager>();
            gmGo.AddComponent<AudioSource>();
            gmGo.AddComponent<Sfx>();
            var vfxComp = gmGo.AddComponent<Vfx>();
            SetFields(vfxComp, ("puffPrefab", vfx.puff), ("sparkPrefab", vfx.spark), ("sparklePrefab", vfx.sparkle));

            var spawnerGo = new GameObject("EnemySpawner");
            spawnerGo.transform.SetParent(managers, false);
            var spawner = spawnerGo.AddComponent<EnemySpawner>();
            var enemiesParent = new GameObject("Monstros").transform;
            // As ondas reais vêm da Campanha em tempo de execução; aqui fica a Noite 1 só para referência no Inspector.
            NightDefinition first = campaign.Get(0);
            spawner.SetWaves(new List<Wave>(first.waves));
            SetFields(spawner, ("initialDelay", first.initialDelay), ("enemiesParent", enemiesParent));

            var moonlightGo = new GameObject("MoonlightSpawner");
            moonlightGo.transform.SetParent(managers, false);
            var moonlight = moonlightGo.AddComponent<MoonlightSpawner>();
            SetFields(moonlight, ("orbPrefab", orb), ("moonbeamPrefab", beam));

            var currencyGo = new GameObject("CurrencyManager");
            currencyGo.transform.SetParent(managers, false);
            var currency = currencyGo.AddComponent<CurrencyManager>();

            var placementGo = new GameObject("ToyPlacementController");
            placementGo.transform.SetParent(managers, false);
            var placement = placementGo.AddComponent<ToyPlacementController>();

            SetFields(game, ("spawner", spawner), ("campaign", campaign), ("currency", currency), ("placement", placement), ("moonlight", moonlight));

            // --- UI
            BuildUI(sprites, defs, campaign, currency, placement, grid, cam);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Build Settings: Game.unity como primeira cena.
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool shadows)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            g.isStatic = true;
            return g;
        }

        /// <summary>Instancia um modelo de cenário do Tripo já normalizado (altura e pegada máximas).</summary>
        private static void Prop(Transform parent, string modelId, Vector3 position, float yaw, float height, float footprint, bool alignLongAxisToZ)
        {
            var holder = new GameObject("Prop_" + modelId);
            holder.transform.SetParent(parent, false);
            Transform visual = BuildVisual(holder, modelId, height, footprint, yaw, () => Placeholder(
                (PrimitiveType.Cube, new Vector3(0, 0.5f, 0), Vector3.one, new Color(0.5f, 0.45f, 0.6f))));

            if (alignLongAxisToZ)
            {
                // Garante que o lado comprido (ex.: comprimento da cama) fique ao longo do eixo Z.
                Transform model = visual.Find("Model");
                Bounds b = MeshBounds(model);
                if (b.size.x > b.size.z)
                {
                    model.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.localRotation;
                    model.localPosition = Vector3.zero;
                    model.localScale = Vector3.one;
                    FitModel(model, height, footprint);
                }
            }

            holder.transform.position = position;
            foreach (Transform t in holder.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        }

        private static void CreatePostProcessVolume()
        {
            string path = Gen + "/NightVolumeProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(new Color(0.05f, 0.02f, 0.15f));
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.saturation.Override(12f);
            color.contrast.Override(8f);
            foreach (VolumeComponent c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volGo = new GameObject("Global Volume (Pós-processamento)");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        // ======================================================================= UI

        private static readonly Color PanelColor = new Color(0.1f, 0.07f, 0.22f, 0.88f);
        private static readonly Color CardColor = new Color(0.98f, 0.93f, 0.82f, 1f);
        private static readonly Color Gold = new Color(1f, 0.83f, 0.3f, 1f);

        private static void BuildUI(Sprites sprites, List<ToyDefinition> defs, Campaign campaign, CurrencyManager currency, ToyPlacementController placement,
            GridManager grid, Camera cam)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var module = esGo.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            var canvasGo = new GameObject("Canvas (UI)");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var hud = canvasGo.AddComponent<HUDController>();
            RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();

            // ---------- HUD (durante o jogo)
            RectTransform hudRoot = Stretch(UIObject("HUD", canvasRt));

            // Barra superior esquerda: Luz + cards + ferramenta de guardar.
            RectTransform bar = UIObject("BarraDeBrinquedos", hudRoot);
            Anchor(bar, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -16), new Vector2(0, 170));
            Image barBg = bar.gameObject.AddComponent<Image>();
            barBg.sprite = sprites.rounded;
            barBg.type = Image.Type.Sliced;
            barBg.color = PanelColor;
            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = bar.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Contador de Luz
            RectTransform lightBox = UIObject("Luz", bar);
            lightBox.sizeDelta = new Vector2(130, 146);
            Image lightBg = lightBox.gameObject.AddComponent<Image>();
            lightBg.sprite = sprites.rounded;
            lightBg.type = Image.Type.Sliced;
            lightBg.color = new Color(0.05f, 0.04f, 0.12f, 0.9f);
            RectTransform glow = UIObject("Brilho", lightBox);
            Anchor(glow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 4), new Vector2(110, 110));
            Image glowImg = glow.gameObject.AddComponent<Image>();
            glowImg.sprite = sprites.glow;
            glowImg.color = new Color(1f, 0.85f, 0.35f, 0.9f);
            glowImg.raycastTarget = false;
            RectTransform orbIcon = UIObject("IconeLuz", lightBox);
            Anchor(orbIcon, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(54, 54));
            Image orbImg = orbIcon.gameObject.AddComponent<Image>();
            orbImg.sprite = sprites.circle;
            orbImg.color = new Color(1f, 0.93f, 0.6f);
            orbImg.raycastTarget = false;
            TMP_Text lightLabel = Text("Valor", lightBox, "150", 44, TextAlignmentOptions.Center, Color.white);
            Anchor(lightLabel.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(0, 54));
            SetFields(currency, ("lightLabel", lightLabel), ("collectTarget", orbIcon));

            // Cards
            var cards = new List<ToyCardUI>();
            for (int i = 0; i < defs.Count; i++)
                cards.Add(BuildCard(bar, defs[i], i + 1, sprites));

            // Ferramenta de guardar brinquedo
            RemoveToolButton removeTool = BuildRemoveTool(bar, sprites);

            SetFields(placement, ("grid", grid), ("cam", cam), ("removeTool", removeTool));
            var so = new SerializedObject(placement);
            SerializedProperty cardsProp = so.FindProperty("cards");
            cardsProp.arraySize = cards.Count;
            for (int i = 0; i < cards.Count; i++) cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            // Painel de ondas (canto superior direito)
            RectTransform wavePanel = UIObject("Ondas", hudRoot);
            Anchor(wavePanel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -16), new Vector2(420, 96));
            Image waveBg = wavePanel.gameObject.AddComponent<Image>();
            waveBg.sprite = sprites.rounded;
            waveBg.type = Image.Type.Sliced;
            waveBg.color = PanelColor;
            TMP_Text waveLabel = Text("Texto", wavePanel, "Noite 1  •  Prepare-se...", 30, TextAlignmentOptions.Center, Color.white);
            Anchor(waveLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-20, 44));
            waveLabel.enableAutoSizing = true;
            waveLabel.fontSizeMin = 18;
            waveLabel.fontSizeMax = 30;
            waveLabel.textWrappingMode = TextWrappingModes.NoWrap;
            RectTransform barBack = UIObject("BarraFundo", wavePanel);
            Anchor(barBack, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-40, 22));
            Image backImg = barBack.gameObject.AddComponent<Image>();
            backImg.sprite = sprites.rounded;
            backImg.type = Image.Type.Sliced;
            backImg.color = new Color(0f, 0f, 0f, 0.5f);
            RectTransform barFill = Stretch(UIObject("Progresso", barBack), 3);
            Image fillImg = barFill.gameObject.AddComponent<Image>();
            fillImg.sprite = sprites.rounded;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 0f;
            fillImg.color = new Color(0.65f, 0.45f, 1f);

            // Dica de controles (rodapé)
            TMP_Text hint = Text("Dica", hudRoot, "Números: escolher brinquedo   •   R: guardar brinquedo   •   Botão direito/Esc: cancelar", 22, TextAlignmentOptions.Center, new Color(1, 1, 1, 0.75f));
            Anchor(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(0, 34));

            // Aviso central
            RectTransform banner = UIObject("Aviso", hudRoot);
            Anchor(banner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 220));
            var bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;
            TMP_Text bannerText = Text("Texto", banner, "", 60, TextAlignmentOptions.Center, Gold);
            Stretch(bannerText.rectTransform);
            bannerText.enableAutoSizing = true;
            bannerText.fontSizeMin = 30;
            bannerText.fontSizeMax = 60;

            // ---------- Menu inicial (arte de título do Nano Banana)
            RectTransform menu = Stretch(UIObject("MenuInicial", canvasRt));
            Image menuBg = menu.gameObject.AddComponent<Image>();
            menuBg.color = new Color(0.02f, 0.02f, 0.06f, 1f);
            RectTransform art = UIObject("ArteTitulo", menu);
            Stretch(art);
            Image artImg = art.gameObject.AddComponent<Image>();
            artImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{UIArtDir}/title_art.png");
            artImg.preserveAspect = false;
            artImg.raycastTarget = false;
            var arf = art.gameObject.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            arf.aspectRatio = artImg.sprite != null ? artImg.sprite.rect.width / artImg.sprite.rect.height : 16f / 9f;
            RectTransform shade = UIObject("Sombra", menu);
            Anchor(shade, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 470));
            Image shadeImg = shade.gameObject.AddComponent<Image>();
            shadeImg.color = new Color(0.02f, 0.01f, 0.08f, 0.76f);
            shadeImg.raycastTarget = false;

            // Cards das noites (Noite 1 ... Noite 5)
            RectTransform nightRow = UIObject("Noites", menu);
            Anchor(nightRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(1400, 124));
            var rowLayout = nightRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 18;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            var nightButtons = new List<NightButtonUI>();
            for (int i = 0; i < campaign.Count; i++)
                nightButtons.Add(BuildNightCard(nightRow, campaign.nights[i], sprites));

            TMP_Text nightDescription = Text("Descricao", menu, "", 27, TextAlignmentOptions.Center, Color.white);
            Anchor(nightDescription.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 152), new Vector2(1500, 140));
            nightDescription.enableAutoSizing = true;
            nightDescription.fontSizeMin = 18;
            nightDescription.fontSizeMax = 27;

            Button startButton = BigButton("BotaoComecar", menu, "Começar Noite 1", sprites, new Vector2(0, 30));
            TMP_Text startLabel = startButton.GetComponentInChildren<TMP_Text>();

            // ---------- Derrota e vitória (dois botões cada)
            ResultPanel("PainelDerrota", canvasRt, sprites, "Os monstros alcançaram a cama!",
                "Tente outra combinação de brinquedos.", "Tentar de Novo", "Menu", new Color(0.35f, 0.05f, 0.12f, 0.75f),
                out GameObject losePanel, out Button retry, out Button loseMenu, out _, out _);
            ResultPanel("PainelVitoria", canvasRt, sprites, "Amanheceu!",
                "A criança dormiu tranquila a noite toda.", "Próxima Noite", "Menu", new Color(0.95f, 0.7f, 0.3f, 0.35f),
                out GameObject winPanel, out Button nextNight, out Button winMenu, out TMP_Text winTitle, out TMP_Text winSubtitle);

            SetFields(hud, ("menuPanel", menu.gameObject), ("hudRoot", hudRoot.gameObject), ("gameOverPanel", losePanel), ("victoryPanel", winPanel),
                ("nightDescription", nightDescription), ("startButton", startButton), ("startButtonLabel", startLabel),
                ("retryButton", retry), ("loseMenuButton", loseMenu),
                ("victoryTitle", winTitle), ("victorySubtitle", winSubtitle), ("nextNightButton", nextNight), ("winMenuButton", winMenu),
                ("waveLabel", waveLabel), ("nightProgressFill", fillImg), ("bannerText", bannerText), ("bannerGroup", bannerGroup));
            var hudSo = new SerializedObject(hud);
            SerializedProperty nightsProp = hudSo.FindProperty("nightButtons");
            nightsProp.arraySize = nightButtons.Count;
            for (int i = 0; i < nightButtons.Count; i++) nightsProp.GetArrayElementAtIndex(i).objectReferenceValue = nightButtons[i];
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            losePanel.SetActive(false);
            winPanel.SetActive(false);
        }

        private static NightButtonUI BuildNightCard(RectTransform parent, NightDefinition night, Sprites sprites)
        {
            RectTransform rt = UIObject("Card_" + night.title, parent);
            rt.sizeDelta = new Vector2(250, 124);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = sprites.rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.2f, 0.14f, 0.4f, 0.95f);
            Button button = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = button.colors;
            cb.highlightedColor = new Color(1.25f, 1.2f, 1.4f);
            cb.pressedColor = new Color(0.8f, 0.75f, 0.9f);
            cb.disabledColor = Color.white; // o CanvasGroup já apaga as noites bloqueadas
            button.colors = cb;

            TMP_Text title = Text("Titulo", rt, night.title, 36, TextAlignmentOptions.Center, Gold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-16, 48));
            TMP_Text subtitle = Text("Subtitulo", rt, night.subtitle, 22, TextAlignmentOptions.Center, Color.white);
            Anchor(subtitle.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(-20, 52));
            subtitle.enableAutoSizing = true;
            subtitle.fontSizeMin = 14;
            subtitle.fontSizeMax = 22;

            RectTransform frameRt = Stretch(UIObject("Selecao", rt), -5);
            Image frame = frameRt.gameObject.AddComponent<Image>();
            frame.sprite = sprites.roundedOutline;
            frame.type = Image.Type.Sliced;
            frame.color = Gold;
            frame.raycastTarget = false;
            frame.enabled = false;

            // Selo dourado de "noite concluída"
            RectTransform badgeRt = UIObject("Concluida", rt);
            Anchor(badgeRt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-14, -14), new Vector2(28, 28));
            Image badge = badgeRt.gameObject.AddComponent<Image>();
            badge.sprite = sprites.circle;
            badge.color = Gold;
            badge.raycastTarget = false;
            badge.enabled = false;

            var ui = rt.gameObject.AddComponent<NightButtonUI>();
            SetFields(ui, ("button", button), ("group", group), ("titleLabel", title), ("subtitleLabel", subtitle),
                ("selectionFrame", frame), ("completedBadge", badge));
            return ui;
        }

        private static ToyCardUI BuildCard(RectTransform parent, ToyDefinition def, int hotkey, Sprites sprites)
        {
            RectTransform card = UIObject("Card_" + def.DisplayName, parent);
            card.sizeDelta = new Vector2(124, 146);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.sprite = sprites.rounded;
            bg.type = Image.Type.Sliced;
            bg.color = CardColor;
            Button button = card.gameObject.AddComponent<Button>();
            ColorBlock cb = button.colors;
            cb.highlightedColor = new Color(1f, 1f, 0.85f);
            cb.pressedColor = new Color(0.85f, 0.8f, 0.7f);
            button.colors = cb;

            RectTransform iconRt = UIObject("Icone", card);
            Anchor(iconRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(104, 92));
            Image icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = def.icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text nameLabel = Text("Nome", card, def.DisplayName, 17, TextAlignmentOptions.Center, new Color(0.25f, 0.15f, 0.35f), false);
            Anchor(nameLabel.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(-6, 22));
            nameLabel.enableAutoSizing = true;
            nameLabel.fontSizeMin = 11;
            nameLabel.fontSizeMax = 17;

            RectTransform costBg = UIObject("CustoFundo", card);
            Anchor(costBg, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(-14, 28));
            Image costImg = costBg.gameObject.AddComponent<Image>();
            costImg.sprite = sprites.rounded;
            costImg.type = Image.Type.Sliced;
            costImg.color = new Color(0.16f, 0.1f, 0.3f, 1f);
            costImg.raycastTarget = false;
            RectTransform costDot = UIObject("Luz", costBg);
            Anchor(costDot, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(18, 18));
            Image dot = costDot.gameObject.AddComponent<Image>();
            dot.sprite = sprites.circle;
            dot.color = Gold;
            dot.raycastTarget = false;
            TMP_Text costLabel = Text("Custo", costBg, def.Cost.ToString(), 24, TextAlignmentOptions.Center, Color.white);
            Stretch(costLabel.rectTransform);
            costLabel.rectTransform.offsetMin = new Vector2(18, 0);

            TMP_Text hotkeyLabel = Text("Atalho", card, hotkey.ToString(), 20, TextAlignmentOptions.Center, Color.white);
            Anchor(hotkeyLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, -2), new Vector2(26, 26));

            RectTransform cdRt = Stretch(UIObject("Recarga", card));
            Image cooldown = cdRt.gameObject.AddComponent<Image>();
            cooldown.sprite = sprites.rounded;
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Vertical;
            cooldown.fillOrigin = (int)Image.OriginVertical.Top;
            cooldown.fillAmount = 0f;
            cooldown.color = new Color(0.05f, 0.03f, 0.12f, 0.6f);
            cooldown.raycastTarget = false;

            RectTransform frameRt = Stretch(UIObject("Selecao", card), -5);
            Image frame = frameRt.gameObject.AddComponent<Image>();
            frame.sprite = sprites.roundedOutline;
            frame.type = Image.Type.Sliced;
            frame.color = Gold;
            frame.raycastTarget = false;
            frame.enabled = false;

            var ui = card.gameObject.AddComponent<ToyCardUI>();
            SetFields(ui, ("definition", def), ("button", button), ("icon", icon), ("cooldownOverlay", cooldown), ("selectionFrame", frame),
                ("costLabel", costLabel), ("nameLabel", nameLabel), ("hotkeyLabel", hotkeyLabel));
            return ui;
        }

        private static RemoveToolButton BuildRemoveTool(RectTransform parent, Sprites sprites)
        {
            RectTransform rt = UIObject("Guardar", parent);
            rt.sizeDelta = new Vector2(104, 146);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = sprites.rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.75f, 0.68f, 0.92f, 1f);
            Button button = rt.gameObject.AddComponent<Button>();

            RectTransform iconRt = UIObject("Icone", rt);
            Anchor(iconRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(86, 86));
            Image icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconsDir}/toy_chest.png");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text label = Text("Nome", rt, "Guardar (R)", 17, TextAlignmentOptions.Center, new Color(0.2f, 0.1f, 0.3f), false);
            Anchor(label.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(-6, 34));

            RectTransform frameRt = Stretch(UIObject("Selecao", rt), -5);
            Image frame = frameRt.gameObject.AddComponent<Image>();
            frame.sprite = sprites.roundedOutline;
            frame.type = Image.Type.Sliced;
            frame.color = Gold;
            frame.raycastTarget = false;
            frame.enabled = false;

            var tool = rt.gameObject.AddComponent<RemoveToolButton>();
            SetFields(tool, ("button", button), ("selectionFrame", frame));
            return tool;
        }

        private static void ResultPanel(string name, RectTransform parent, Sprites sprites, string title, string subtitle,
            string primaryText, string secondaryText, Color tint,
            out GameObject panel, out Button primary, out Button secondary, out TMP_Text titleText, out TMP_Text subtitleText)
        {
            RectTransform root = Stretch(UIObject(name, parent));
            Image dim = root.gameObject.AddComponent<Image>();
            dim.color = tint;
            RectTransform box = UIObject("Caixa", root);
            Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 420));
            Image boxImg = box.gameObject.AddComponent<Image>();
            boxImg.sprite = sprites.rounded;
            boxImg.type = Image.Type.Sliced;
            boxImg.color = PanelColor;
            titleText = Text("Titulo", box, title, 58, TextAlignmentOptions.Center, Gold);
            Anchor(titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(-60, 140));
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 36;
            titleText.fontSizeMax = 58;
            subtitleText = Text("Subtitulo", box, subtitle, 30, TextAlignmentOptions.Center, Color.white);
            Anchor(subtitleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -180), new Vector2(-60, 60));
            subtitleText.enableAutoSizing = true;
            subtitleText.fontSizeMin = 20;
            subtitleText.fontSizeMax = 30;

            // Botão principal (dourado) à esquerda e "Menu" (lilás) à direita.
            primary = BigButton("BotaoPrincipal", box, primaryText, sprites, new Vector2(-170, 50));
            secondary = BigButton("BotaoMenu", box, secondaryText, sprites, new Vector2(270, 50), 300f, new Color(0.75f, 0.68f, 0.92f, 1f));
            panel = root.gameObject;
        }

        private static Button BigButton(string name, RectTransform parent, string label, Sprites sprites, Vector2 bottomOffset,
            float width = 460f, Color? color = null)
        {
            RectTransform rt = UIObject(name, parent);
            Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), bottomOffset, new Vector2(width, 110));
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprites.rounded;
            img.type = Image.Type.Sliced;
            img.color = color ?? Gold;
            Button b = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1f, 1f, 0.8f);
            cb.pressedColor = new Color(0.85f, 0.7f, 0.3f);
            b.colors = cb;
            TMP_Text t = Text("Texto", rt, label, 42, TextAlignmentOptions.Center, new Color(0.2f, 0.08f, 0.3f), false);
            Stretch(t.rectTransform);
            return b;
        }

        // ---------- Helpers de UI

        private static RectTransform UIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static TMP_Text Text(string name, Transform parent, string text, float size, TextAlignmentOptions align, Color color, bool outline = true)
        {
            RectTransform rt = UIObject(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            if (outline && fontOutlineMaterial != null) t.fontSharedMaterial = fontOutlineMaterial;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        // ======================================================================= SERIALIZAÇÃO

        /// <summary>Atribui campos [SerializeField] privados por nome (via SerializedObject).</summary>
        private static void SetFields(Object target, params (string name, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p == null)
                {
                    Debug.LogError($"[SleepDefenders] Campo '{name}' não encontrado em {target.GetType().Name}");
                    continue;
                }
                switch (value)
                {
                    case null: p.objectReferenceValue = null; break;
                    case int i: p.intValue = i; break;
                    case float f: p.floatValue = f; break;
                    case bool b: p.boolValue = b; break;
                    case string s: p.stringValue = s; break;
                    case Color c: p.colorValue = c; break;
                    case Vector2 v2: p.vector2Value = v2; break;
                    case Vector3 v3: p.vector3Value = v3; break;
                    case Component comp: p.objectReferenceValue = comp; break;
                    case Object o: p.objectReferenceValue = o; break;
                    default: Debug.LogError($"[SleepDefenders] Tipo não suportado para '{name}': {value.GetType()}"); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Na primeira abertura do projeto (sem a cena do jogo) ou depois de uma atualização que precisa
    /// reconstruir (ex.: campanha de noites ainda não gerada), oferece construir tudo.
    /// </summary>
    [InitializeOnLoad]
    internal static class SleepDefendersFirstRun
    {
        private static bool NeedsBuild =>
            !File.Exists(SleepDefendersBuilder.ScenePath) || !File.Exists(SleepDefendersBuilder.CampaignPath);

        static SleepDefendersFirstRun()
        {
            if (Application.isBatchMode || !NeedsBuild) return;
            if (SessionState.GetBool("SleepDefenders.Prompted.v2", false)) return;
            SessionState.SetBool("SleepDefenders.Prompted.v2", true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !NeedsBuild) return;
                bool update = File.Exists(SleepDefendersBuilder.ScenePath);
                string message = update
                    ? "O jogo foi atualizado (5 noites, Ratinho de Corda e novo menu).\nReconstruir agora os prefabs e a cena?"
                    : "Construir agora os prefabs e a cena do jogo?\n(Também disponível no menu Sleep Defenders.)";
                if (!EditorUtility.DisplayDialog("Brinquedos vs. Monstros do Escuro", message, "Construir", "Depois")) return;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                SleepDefendersBuilder.BuildAll(); // a cena nova já fica aberta
            };
        }
    }
}

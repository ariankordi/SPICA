using SPICA.Formats.CtrGfx;
using SPICA.Formats.CtrH3D;
using SPICA.Formats.CtrH3D.Animation;
using SPICA.Formats.CtrH3D.Model;
using SPICA.Formats.CtrH3D.Model.Mesh;
using SPICA.Formats.Generic.COLLADA;
using System;
using System.IO;
using System.Linq;

namespace SPICA.Cli
{
    /// <summary>
    /// Headless front end for SPICA: lists what a CGFX (.bcres/.bcmdl/.bcskla) file holds, or exports
    /// a model and skeletal animations to COLLADA through SPICA's own DAE exporter, the same calls the
    /// SPICA GUI's export makes (https://github.com/KillzXGaming/SPICA, as used by CTR-Studio).
    /// </summary>
    internal static class Program
    {
        private const string Usage =
            "Usage:\n" +
            "  SPICA.Cli list <file>...\n" +
            "  SPICA.Cli export <model> <out-dir> [<anim>...] [--model-name <name>] [--fps <rate>] [--verbose]\n" +
            "\n" +
            "export writes <out-dir>/<model>.dae (mesh and skeleton) and, for each skeletal\n" +
            "animation in the model file and the <anim> files, <out-dir>/<anim>.dae holding the\n" +
            "model plus that one animation. Inputs must already be decompressed (no LZ11).\n" +
            "  --model-name  export this model when the file holds several. Default: the first.\n" +
            "  --fps         frames per second for animation times. Default: 30, SPICA's own value.\n" +
            "  --verbose     print stack traces for failures";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "list")
                {
                    foreach (string path in args.Skip(1))
                    {
                        List(path);
                    }

                    return 0;
                }

                if (args.Length >= 3 && args[0] == "export")
                {
                    return Export(args.Skip(1).ToArray());
                }

                Console.Error.WriteLine(Usage);
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"error: {e}");
                return 1;
            }
        }

        private static H3D Open(string path)
        {
            using (FileStream input = File.OpenRead(path))
            {
                return Gfx.OpenAsH3D(input);
            }
        }

        private static void List(string path)
        {
            H3D scene = Open(path);
            Console.WriteLine(path);
            foreach (H3DModel model in scene.Models)
            {
                Console.WriteLine($"  model {model.Name}: {model.Meshes.Count} meshes, {model.Skeleton.Count} bones");
                foreach (H3DMesh mesh in model.Meshes)
                {
                    string material = mesh.MaterialIndex < model.Materials.Count ? model.Materials[mesh.MaterialIndex].Name : "?";
                    Console.WriteLine($"    mesh: material {material}, type {mesh.Type}");
                }
            }

            foreach (H3DAnimation animation in scene.SkeletalAnimations)
            {
                Console.WriteLine($"  skeletal animation {animation.Name}: {animation.FramesCount} frames, {animation.Elements.Count} bones");
            }
        }

        private static int Export(string[] args)
        {
            string modelPath = args[0];
            string outFolder = args[1];
            string modelName = null;
            bool verbose = false;
            // The files store no frame rate; SPICA's DAE exporter has always assumed 30.
            float framesPerSecond = 30f;
            var animationPaths = new System.Collections.Generic.List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--model-name" && i + 1 < args.Length)
                {
                    modelName = args[++i];
                }
                else if (args[i] == "--fps" && i + 1 < args.Length)
                {
                    framesPerSecond = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    if (!(framesPerSecond > 0f))
                    {
                        throw new ArgumentException("--fps must be positive");
                    }
                }
                else if (args[i] == "--verbose")
                {
                    verbose = true;
                }
                else if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"unknown option {args[i]}");
                }
                else
                {
                    animationPaths.Add(args[i]);
                }
            }

            H3D scene = Open(modelPath);
            int modelIndex = modelName == null ? 0 : scene.Models.Find(modelName);
            if (scene.Models.Count == 0 || modelIndex < 0)
            {
                throw new InvalidDataException(modelName == null ? $"{modelPath} has no model" : $"no model named {modelName}");
            }

            int failures = 0;
            foreach (string animationPath in animationPaths)
            {
                try
                {
                    H3D animations = Open(animationPath);
                    if (animations.SkeletalAnimations.Count == 0)
                    {
                        Console.Error.WriteLine($"warning: {animationPath} has no skeletal animations");
                    }

                    scene.Merge(animations);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"failed: {animationPath}: {Describe(e, verbose)}");
                    failures++;
                }
            }

            Directory.CreateDirectory(outFolder);
            H3DModel model = scene.Models[modelIndex];
            string modelOut = ChildPath(outFolder, model.Name, ".dae");
            new DAE(scene, modelIndex).Save(modelOut);
            Console.WriteLine(modelOut);

            for (int animationIndex = 0; animationIndex < scene.SkeletalAnimations.Count; animationIndex++)
            {
                H3DAnimation animation = scene.SkeletalAnimations[animationIndex];
                string animationOut = null;
                string[] missingBones = animation.Elements
                    .Select(element => element.Name)
                    .Where(name => model.Skeleton.Find(name) < 0)
                    .Distinct()
                    .ToArray();
                if (missingBones.Length > 0)
                {
                    Console.Error.WriteLine($"warning: {animation.Name}: dropped channels for bones not in the skeleton: {string.Join(", ", missingBones)}");
                }

                try
                {
                    animationOut = ChildPath(outFolder, animation.Name, ".dae");
                    new DAE(scene, modelIndex, animationIndex, framesPerSecond).Save(animationOut);
                    Console.WriteLine(animationOut);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"failed: {animationOut ?? animation.Name}: {Describe(e, verbose)}");
                    failures++;
                }
            }

            return failures == 0 ? 0 : 1;
        }

        private static string Describe(Exception e, bool verbose)
        {
            return verbose ? e.ToString() : $"{e.GetType().Name}: {e.Message}";
        }

        /// <summary>
        /// Builds a path for a name read from the input file, so a crafted name (e.g. "../x") can't
        /// write outside the output folder. Same as BrawlCrate.Cli's ChildPath in the BrawlCrate fork.
        /// </summary>
        private static string ChildPath(string folder, string name, string extension)
        {
            char[] invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':' }).ToArray();
            string safe = new string((name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            if (safe.Trim('.', ' ').Length == 0)
            {
                safe = "_" + safe;
            }

            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, safe + extension));
            if (!path.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"unsafe name in file: {name}");
            }

            return path;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spoomples.Extensions.WildcardImporter;

using System.IO;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

// AST Node Records
public abstract record MaskSpecifier;

// Basic mask specifiers
public record YoloMask(string ModelName, string ClassFilter, double? Threshold = null) : MaskSpecifier;
public record ClipSegMask(string Text, double? Threshold = null) : MaskSpecifier;
public record ThresholdMask(MaskSpecifier BaseMask, double ThresholdMax) : MaskSpecifier;
public record BoxMask(double X, double Y, double Width, double Height) : MaskSpecifier;

// Mask modifiers/operators
public record IndexedMask(MaskSpecifier Mask, int Index) : MaskSpecifier;
public record InvertMask(MaskSpecifier Mask) : MaskSpecifier;
public record GrowMask(MaskSpecifier Mask, int Pixels) : MaskSpecifier;
public record UnionMask(MaskSpecifier Left, MaskSpecifier Right) : MaskSpecifier;
public record IntersectMask(MaskSpecifier Left, MaskSpecifier Right) : MaskSpecifier;
public record BoundingBoxMask(MaskSpecifier Mask) : MaskSpecifier;

// Detailer parameters
public record DetailerParams(int Blur, double Creativity);

public static class Detailer
{
    public const string DIRECTIVE = "wcdetailer";
    
    public static MaskSpecifier ParseMaskSpecifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or empty", nameof(input));

        var parser = new DetailerMaskParser(input.Trim());
        return parser.ParseExpression();
    }

    public static (DetailerParams, string) ParseDetailerParams(WorkflowGenerator g, string input)
    {
        //default blur: g.UserInput.Get(T2IParamTypes.SegmentMaskBlur, 10)
        // default creativity: 0.5
        // Use Regex top strip optional leading [...] from front of input and split its contents by comma, then by :
        // so we can parse strings like this: [blur:10, creativity:0.5] if it is in the input, if it is
        // not in the input we will use defaults
        
        int defaultBlur = g.UserInput.Get(T2IParamTypes.SegmentMaskBlur, 10);
        double defaultCreativity = 0.5;
        
        // Look for parameter block at the start: [param1:value1,param2:value2]
        var match = System.Text.RegularExpressions.Regex.Match(input, @"^\[\s*([^\]]+)\s*\](.*)");
        
        if (!match.Success)
        {
            // No parameter block found, use defaults
            return (new DetailerParams(defaultBlur, defaultCreativity), input);
        }
        
        string paramString = match.Groups[1].Value;
        string remainingInput = match.Groups[2].Value;
        
        int blur = defaultBlur;
        double creativity = defaultCreativity;
        
        // Split by comma and parse each parameter
        string[] parameters = paramString.Split(',');
        foreach (string param in parameters)
        {
            string[] parts = param.Split(':', 2);
            if (parts.Length == 2)
            {
                string key = parts[0].Trim().ToLowerInvariant();
                string value = parts[1].Trim();
                
                switch (key)
                {
                    case "blur":
                        if (int.TryParse(value, out int blurValue))
                            blur = blurValue;
                        break;
                    case "creativity":
                        if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double creativityValue))
                            creativity = creativityValue;
                        break;
                }
            }
        }
        
        return (new DetailerParams(blur, creativity), remainingInput);
    }
    
    private static string GenerateMaskNodes(WorkflowGenerator g, MaskSpecifier maskSpecifier, int objectIndex = 0)
    {
        switch (maskSpecifier)
        {
            case YoloMask yoloMask:
                return g.CreateNode("SwarmYoloDetection", new JObject()
                {
                    ["image"] = g.FinalImageOut,
                    ["model_name"] = yoloMask.ModelName,
                    ["index"] = objectIndex,
                    ["class_filter"] = yoloMask.ClassFilter,
                    ["sort_order"] = g.UserInput.Get(T2IParamTypes.SegmentSortOrder, "left-right"),
                    ["threshold"] = Math.Abs(yoloMask.Threshold ?? 0.25),
                });
            case ClipSegMask clipSegMask:
                return g.CreateNode("SwarmClipSeg", new JObject()
                {
                    ["images"] = g.FinalImageOut,
                    ["match_text"] = clipSegMask.Text,
                    ["threshold"] = Math.Abs(clipSegMask.Threshold ?? 0.5),
                });
            case ThresholdMask thresholdMask:
                return g.CreateNode("SwarmMaskThreshold", new JObject()
                {
                    ["mask"] = GenerateMaskNodes(g, thresholdMask.BaseMask),
                    ["min"] = 0.01,
                    ["max"] = thresholdMask.ThresholdMax
                });
            case BoxMask boxMask:
                return g.CreateNode("SwarmSquareMaskFromPercent", new JObject()
                {
                    ["x"] = boxMask.X,
                    ["y"] = boxMask.Y,
                    ["width"] = boxMask.Width,
                    ["height"] = boxMask.Height,
                    ["strength"] = 1.0,
                });
            case IndexedMask indexedMask:
                if (indexedMask.Mask is YoloMask)
                {
                    // yolo model supports indexing directly.  Just pass the index down
                    return GenerateMaskNodes(g, indexedMask.Mask, indexedMask.Index);
                }
                
                throw new NotImplementedException("Have not implemented indexed masks for non-yolo models yet");
            case InvertMask invertMask:
                return g.CreateNode("InvertMask", new JObject()
                {
                    ["mask"] = new JArray() { GenerateMaskNodes(g, invertMask.Mask), 0 },
                });
            case GrowMask growMask:
                return g.CreateNode("GrowMask", new JObject()
                {
                    ["mask"] = new JArray() { GenerateMaskNodes(g, growMask.Mask), 0 },
                    ["expand"] = growMask.Pixels,
                    ["tapered_corners"] = true
                });
            case UnionMask unionMask:
                return g.CreateNode("WCCompositeMask", new JObject()
                {
                    ["mask_a"] = new JArray() { GenerateMaskNodes(g, unionMask.Left), 0 },
                    ["mask_b"] = new JArray() { GenerateMaskNodes(g, unionMask.Right), 0 },
                    ["op"] = "max",
                });
            case IntersectMask intersectMask:
                return g.CreateNode("WCCompositeMask", new JObject()
                {
                    ["mask_a"] = new JArray() { GenerateMaskNodes(g, intersectMask.Left), 0 },
                    ["mask_b"] = new JArray() { GenerateMaskNodes(g, intersectMask.Right), 0 },
                    ["op"] = "min",
                });
            case BoundingBoxMask boundingBoxMask:
                throw new NotImplementedException("bounding box mask not implemented");
            default:
                throw new NotImplementedException($"Unknown mask type: {maskSpecifier.GetType().Name}");
        }
    }

    public static void Register(string FilePath)
    {
        PromptRegion.RegisterCustomPrefix(DIRECTIVE);
        T2IPromptHandling.PromptTagPostProcessors[DIRECTIVE] = T2IPromptHandling.PromptTagPostProcessors["segment"];
        var NodeFolder = Path.Join(FilePath, "WCNodes");
        ComfyUISelfStartBackend.CustomNodePaths.Add(NodeFolder);
        Logs.Init($"Adding {NodeFolder} to CustomNodePaths");
        
        WorkflowGeneratorSteps.AddStep(g => 
        { 
            PromptRegion.Part[] parts = [.. new PromptRegion(g.UserInput.Get(T2IParamTypes.Prompt, "")).Parts.Where(p => p.Type == PromptRegion.PartType.CustomPart && p.Prefix == DIRECTIVE)];
            if (!parts.IsEmpty())
            {
                if (g.UserInput.Get(T2IParamTypes.OutputIntermediateImages, false))
                {
                    g.CreateImageSaveNode(g.FinalImageOut, g.GetStableDynamicID(50000, 0));
                }
                T2IModel t2iModel = g.FinalLoadedModel;
                JArray model = g.FinalModel, clip = g.FinalClip, vae = g.FinalVae;
                if (g.UserInput.TryGet(T2IParamTypes.SegmentModel, out T2IModel segmentModel))
                {
                    if (segmentModel.ModelClass?.CompatClass != t2iModel.ModelClass?.CompatClass)
                    {
                        g.NoVAEOverride = true;
                    }
                    t2iModel = segmentModel;
                    g.FinalLoadedModel = segmentModel;
                    (t2iModel, model, clip, vae) = g.CreateStandardModelLoader(t2iModel, "Refiner");
                    g.FinalLoadedModel = t2iModel;
                    g.FinalModel = model;
                }
                PromptRegion negativeRegion = new(g.UserInput.Get(T2IParamTypes.NegativePrompt, ""));
                PromptRegion.Part[] negativeParts = [.. negativeRegion.Parts.Where(p => p.Type == PromptRegion.PartType.CustomPart && p.Prefix == DIRECTIVE)];
                int growAmt = g.UserInput.Get(T2IParamTypes.SegmentMaskGrow, 16);
                for (int i = 0; i < parts.Length; i++)
                {
                    PromptRegion.Part part = parts[i];
                    (DetailerParams detailerParams, string maskSpecString) = ParseDetailerParams(g, part.DataText);
                    MaskSpecifier maskSpec = ParseMaskSpecifier(maskSpecString);
                    string segmentNode = GenerateMaskNodes(g, maskSpec);
                    if (detailerParams.Blur > 0)
                    {
                        segmentNode = g.CreateNode("SwarmMaskBlur", new JObject()
                        {
                            ["mask"] = new JArray() { segmentNode, 0 },
                            ["blur_radius"] = detailerParams.Blur,
                            ["sigma"] = 1
                        });
                    }
                    // grow if top-level mask does not have a grow modifier
                    if (growAmt > 0 && !(maskSpec is GrowMask))
                    {
                        segmentNode = g.CreateNode("GrowMask", new JObject()
                        {
                            ["mask"] = new JArray() { segmentNode, 0 },
                            ["expand"] = growAmt,
                            ["tapered_corners"] = true
                        });
                    }
                    if (g.UserInput.Get(T2IParamTypes.SaveSegmentMask, false))
                    {
                        string imageNode = g.CreateNode("MaskToImage", new JObject()
                        {
                            ["mask"] = new JArray() { segmentNode, 0 }
                        });
                        g.CreateImageSaveNode([imageNode, 0], g.GetStableDynamicID(50000, 0));
                    }
                    int oversize = g.UserInput.Get(T2IParamTypes.SegmentMaskOversize, 16);
                    g.MaskShrunkInfo = g.CreateImageMaskCrop([segmentNode, 0], g.FinalImageOut, oversize, vae, g.FinalLoadedModel, thresholdMax: g.UserInput.Get(T2IParamTypes.SegmentThresholdMax, 1));
                    g.EnableDifferential();
                    if (part.ContextID > 0)
                    {
                        (model, clip) = g.LoadLorasForConfinement(part.ContextID, g.FinalModel, clip);
                    }
                    JArray prompt = g.CreateConditioning(part.Prompt, clip, t2iModel, true);
                    string neg = negativeParts.FirstOrDefault(p => p.DataText == part.DataText)?.Prompt ?? negativeRegion.GlobalPrompt;
                    JArray negPrompt = g.CreateConditioning(neg, clip, t2iModel, false);
                    int steps = g.UserInput.Get(T2IParamTypes.SegmentSteps, g.UserInput.Get(T2IParamTypes.RefinerSteps, g.UserInput.Get(T2IParamTypes.Steps)));
                    int startStep = (int)Math.Round(steps * (1 - detailerParams.Creativity));
                    long seed = g.UserInput.Get(T2IParamTypes.Seed) + 2 + i;
                    double cfg = g.UserInput.Get(T2IParamTypes.SegmentCFGScale, g.UserInput.Get(T2IParamTypes.RefinerCFGScale, g.UserInput.Get(T2IParamTypes.CFGScale)));
                    string sampler = g.CreateKSampler(model, prompt, negPrompt, [g.MaskShrunkInfo.MaskedLatent, 0], cfg, steps, startStep, 10000, seed, false, true);
                    string decoded = g.CreateVAEDecode(vae, [sampler, 0]);
                    g.FinalImageOut = g.RecompositeCropped(g.MaskShrunkInfo.BoundsNode, [g.MaskShrunkInfo.CroppedMask, 0], g.FinalImageOut, [decoded, 0]);
                    g.MaskShrunkInfo = new(null, null, null, null);
                }
            }
        },
            // same priority as <segment>
            5);
    }
}
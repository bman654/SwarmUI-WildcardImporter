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

// Context for tracking generated nodes to enable reuse
public class MaskNodeContext
{
    private readonly Dictionary<MaskSpecifier, string> _maskNodes = new();
    private readonly Dictionary<(YoloMask, int), string> _yoloNodes = new();
    
    public bool TryGetNode(MaskSpecifier maskSpecifier, out string nodeId)
    {
        return _maskNodes.TryGetValue(maskSpecifier, out nodeId);
    }
    
    public bool TryGetYoloNode(YoloMask yoloMask, int objectIndex, out string nodeId)
    {
        return _yoloNodes.TryGetValue((yoloMask, objectIndex), out nodeId);
    }
    
    public void AddNode(MaskSpecifier maskSpecifier, string nodeId)
    {
        _maskNodes[maskSpecifier] = nodeId;
    }
    
    public void AddYoloNode(YoloMask yoloMask, int objectIndex, string nodeId)
    {
        _yoloNodes[(yoloMask, objectIndex)] = nodeId;
    }
}

// Basic mask specifiers
public record YoloMask(string ModelName, string ClassFilter, double? Threshold = null) : MaskSpecifier;
public record ClipSegMask(string Text, double? Threshold = null) : MaskSpecifier;
public record ThresholdMask(MaskSpecifier BaseMask, double ThresholdMax) : MaskSpecifier;
public record BoxMask(double X, double Y, double Width, double Height) : MaskSpecifier;
public record CircleMask(double X, double Y, double Radius) : MaskSpecifier;

// Mask modifiers/operators
public record IndexedMask(MaskSpecifier Mask, int Index) : MaskSpecifier;
public record InvertMask(MaskSpecifier Mask) : MaskSpecifier;
public record GrowMask(MaskSpecifier Mask, int Pixels) : MaskSpecifier;
public record UnionMask(MaskSpecifier Left, MaskSpecifier Right) : MaskSpecifier;
public record IntersectMask(MaskSpecifier Left, MaskSpecifier Right) : MaskSpecifier;
public record BoundingBoxMask(MaskSpecifier Mask) : MaskSpecifier;
public record BoundingCircleMask(MaskSpecifier Mask) : MaskSpecifier;

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
        
        int defaultBlur = g.UserInput.Get(DetailMaskBlur, 10);
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
    
    // Public entry point that establishes a new context for node reuse
    public static string GenerateMaskNodes(WorkflowGenerator g, MaskSpecifier maskSpecifier, int objectIndex = 0)
    {
        var context = new MaskNodeContext();
        return GenerateMaskNodes(g, maskSpecifier, context, objectIndex);
    }
    
    private static string GenerateMaskNodes(WorkflowGenerator g, MaskSpecifier maskSpecifier, MaskNodeContext context, int objectIndex = 0)
    {
        // For YoloMask, we need to check the YOLO-specific dictionary that includes objectIndex
        if (maskSpecifier is YoloMask yoloMask)
        {
            if (context.TryGetYoloNode(yoloMask, objectIndex, out string yoloNodeId))
            {
                return yoloNodeId;
            }
            
            string resultYoloNodeId = GenerateMaskNodesInternal(g, maskSpecifier, context, objectIndex);
            // Cache the YoloMask node with its objectIndex
            context.AddYoloNode(yoloMask, objectIndex, resultYoloNodeId);
            return resultYoloNodeId;
        }
        
        // For non-YOLO masks, use the general dictionary
        if (context.TryGetNode(maskSpecifier, out string nodeId))
        {
            return nodeId;
        }
        
        string resultNodeId = GenerateMaskNodesInternal(g, maskSpecifier, context, objectIndex);
        context.AddNode(maskSpecifier, resultNodeId);
        return resultNodeId;
    }

    private static string GenerateMaskNodesInternal(WorkflowGenerator g, MaskSpecifier maskSpecifier, MaskNodeContext context, int objectIndex = 0)
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
                    ["sort_order"] = g.UserInput.Get(DetailSortOrder, "left-right"),
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
                    ["mask"] = GenerateMaskNodes(g, thresholdMask.BaseMask, context),
                    ["min"] = 0.01,
                    ["max"] = thresholdMask.ThresholdMax
                });
            case BoxMask boxMask:
                return g.CreateNode("WCBoxMask", new JObject()
                {
                    ["image"] = g.FinalImageOut,
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
                    return GenerateMaskNodes(g, indexedMask.Mask, context, indexedMask.Index);
                }
                
                // For non-YOLO masks, use WCSeparateMaskComponents to separate and select features
                string baseMaskNode = GenerateMaskNodes(g, indexedMask.Mask, context);
                int featureThreshold = g.UserInput.Get(DetailFeatureThreshold, 16);
                
                // Optionally grow the mask to merge nearby features before separation
                string grownMaskNode = baseMaskNode;
                if (featureThreshold > 0)
                {
                    grownMaskNode = g.CreateNode("GrowMask", new JObject()
                    {
                        ["mask"] = new JArray() { baseMaskNode, 0 },
                        ["expand"] = (featureThreshold + 1) / 2,
                        ["tapered_corners"] = true
                    });
                }
                
                // Use WCSeparateMaskComponents to separate and select the indexed feature
                return g.CreateNode("WCSeparateMaskComponents", new JObject()
                {
                    ["mask"] = new JArray() { grownMaskNode, 0 },
                    ["sort_order"] = g.UserInput.Get(DetailSortOrder, "left-right"),
                    ["index"] = indexedMask.Index - 1,
                    ["orig_mask"] = new JArray() { baseMaskNode, 0 }
                });
            case InvertMask invertMask:
                return g.CreateNode("InvertMask", new JObject()
                {
                    ["mask"] = new JArray() { GenerateMaskNodes(g, invertMask.Mask, context), 0 },
                });
            case GrowMask growMask:
                var maskNode = GenerateMaskNodes(g, growMask.Mask, context);
                if (growMask.Pixels == 0)
                {
                    return maskNode;
                }
                return g.CreateNode("GrowMask", new JObject()
                {
                    ["mask"] = new JArray() { maskNode, 0 },
                    ["expand"] = growMask.Pixels,
                    ["tapered_corners"] = true
                });
            case UnionMask unionMask:
                return g.CreateNode("WCCompositeMask", new JObject()
                {
                    ["mask_a"] = new JArray() { GenerateMaskNodes(g, unionMask.Left, context), 0 },
                    ["mask_b"] = new JArray() { GenerateMaskNodes(g, unionMask.Right, context), 0 },
                    ["op"] = "max",
                });
            case IntersectMask intersectMask:
                return g.CreateNode("WCCompositeMask", new JObject()
                {
                    ["mask_a"] = new JArray() { GenerateMaskNodes(g, intersectMask.Left, context), 0 },
                    ["mask_b"] = new JArray() { GenerateMaskNodes(g, intersectMask.Right, context), 0 },
                    ["op"] = "min",
                });
            case BoundingBoxMask boundingBoxMask:
                return g.CreateNode("WCBoundingBoxMask", new JObject()
                {
                    ["mask"] = new JArray() { GenerateMaskNodes(g, boundingBoxMask.Mask, context), 0 },
                });
            case BoundingCircleMask boundingCircleMask:
                return g.CreateNode("WCBoundingCircleMask", new JObject()
                {
                    ["mask"] = new JArray() { GenerateMaskNodes(g, boundingCircleMask.Mask, context), 0 },
                });
            case CircleMask circleMask:
                return g.CreateNode("WCCircleMask", new JObject()
                {
                    ["image"] = g.FinalImageOut,
                    ["x"] = circleMask.X,
                    ["y"] = circleMask.Y,
                    ["radius"] = circleMask.Radius,
                    ["strength"] = 1.0,
                });
            default:
                throw new NotImplementedException($"Unknown mask type: {maskSpecifier.GetType().Name}");
        }
    }

    private static T2IRegisteredParam<bool> SaveDetailMask, DetailDynamicResolution;
    private static T2IRegisteredParam<string> DetailSortOrder, DetailTargetResolution;
    private static T2IRegisteredParam<int> DetailMaskBlur, DetailMaskGrow, DetailMaskOversize, DetailSteps, DetailFeatureThreshold;
    private static T2IRegisteredParam<double> DetailThresholdMax, DetailCFGScale;
    private static T2IRegisteredParam<T2IModel> DetailModel;
    private static T2IParamGroup GroupDetailRefining, GroupDetailOverrides;
    public static void AddT2IParameters()
    {
        GroupDetailRefining = new("WC Detailer", Open: false, OrderPriority: 9.5, IsAdvanced: false);
        SaveDetailMask = T2IParamTypes.Register<bool>(new("WC Save Detail Mask", $"If checked, any usage of '<{DIRECTIVE}:>' syntax in prompts will save the generated mask in output.",
            "false", IgnoreIf: "false", Group: GroupDetailRefining, OrderPriority: 3
            ));
        DetailMaskBlur = T2IParamTypes.Register<int>(new("WC Detail Mask Blur", $"Amount of blur to apply to the detail mask before using it.\nThis is for '<{DIRECTIVE}:>' syntax usage.\nDefaults to 10.\nCan be overridden by '<{DIRECTIVE}:[blur:10]>' syntax.",
            "10", Min: 0, Max: 64, Group: GroupDetailRefining, Examples: ["0", "4", "8", "16"], Toggleable: true, OrderPriority: 4
            ));
        DetailMaskGrow = T2IParamTypes.Register<int>(new("WC Detail Mask Grow", $"Number of pixels of grow the detail mask by.\nThis is for '<{DIRECTIVE}:>' syntax usage.\nDefaults to 16.\nCan be overridden by '<{DIRECTIVE}:somemask + 16>' syntax.",
            "16", Min: 0, Max: 512, Group: GroupDetailRefining, Examples: ["0", "4", "8", "16", "32"], Toggleable: true, OrderPriority: 5
            ));
        DetailMaskOversize = T2IParamTypes.Register<int>(new("WC Detail Mask Oversize", $"How wide a detail mask should be oversized by.\nLarger values include more context to get more accurate inpaint,\nand smaller values get closer to get better details.",
            "16", Min: 0, Max: 512, Toggleable: true, OrderPriority: 5.5, Group: GroupDetailRefining, Examples: ["0", "8", "32"]
            ));
        DetailFeatureThreshold = T2IParamTypes.Register<int>(new("WC Detail Feature Threshold", $"Number of pixels that can separate masked areas while still considering them as a single feature.\nUsed when using the indexing syntax to extract a feature from a non-YOLO mask.",
            "16", Min: 0, Max: 128, Group: GroupDetailRefining, Examples: ["0", "4", "8", "16"], Toggleable: true, OrderPriority: 5.7
            ));
        DetailThresholdMax = T2IParamTypes.Register<double>(new("WC Detail Threshold Max", "Maximum mask match value of a detail mask before clamping.\nLower values force more of the mask to be counted as maximum masking.\nToo-low values may include unwanted areas of the image.\nHigher values may soften the mask.",
            "1", Min: 0, Max: 1, Step: 0.05, Toggleable: true, ViewType: ParamViewType.SLIDER, Group: GroupDetailRefining, OrderPriority: 6
            ));
        DetailSortOrder = T2IParamTypes.Register<string>(new("WC Detail Sort Order", $"How to sort detail mask features when using '<{DIRECTIVE}:somemask[1]' syntax with indices.\nFor example: <{DIRECTIVE}:yolo-face_yolov8m-seg_60.pt[2]> with largest-smallest, will select the second largest face feature.",
            "left-right", IgnoreIf: "left-right", GetValues: _ => ["left-right", "right-left", "top-bottom", "bottom-top", "largest-smallest", "smallest-largest"], Group: GroupDetailRefining, OrderPriority: 7
            ));
        DetailTargetResolution = T2IParamTypes.Register<string>(new("WC Detail Target Resolution", "Optional specific target resolution for the detailer.\nThis controls both aspect ratio, and size.\nThis is just a target, the system may fail to exactly hit it.\nIf the mask is on the edge of an image, the aspect may be squished.\nIf unspecified, the aspect ratio of the detection will be used, and the resolution of the model.",
            "1024x1024", Toggleable: true, Group: GroupDetailRefining, OrderPriority: 20
            ));
        DetailDynamicResolution = T2IParamTypes.Register<bool>(new("WC Detail Dynamic Target Resolution", $"If checked, DC Target Resolution will be treated as a minimum resolution hint.\nActual target resolution will be dynamically determined to be roughly at least as many pixels as Target Resolution but with an aspect ratio closer to the mask.\n  This maximizes the amount of pixels available for detailing the masked area.\nIf the mask is higher resolution than the Target, then the mask resolution will be used.",
            "false", IgnoreIf: "false", Group: GroupDetailRefining, OrderPriority: 21
        ));

        GroupDetailOverrides = new("WC Detail Param Overrides", Toggles: false, Open: false, OrderPriority: 50, IsAdvanced: true, Description: "This sub-group of the WC Detailer group contains core-parameter overrides, such as replacing the base Step count or CFG Scale, unique to the WC Detail generation stage.", Parent: GroupDetailRefining);
        DetailModel = T2IParamTypes.Register<T2IModel>(new("WC Detail Model", $"Optionally specify a distinct model to use for '{DIRECTIVE}' values.",
            "", Toggleable: true, Subtype: "Stable-Diffusion", Group: GroupDetailOverrides, OrderPriority: 2, IsAdvanced: true
            ));
        DetailSteps = T2IParamTypes.Register<int>(new("WC Detail Steps", "Alternate Steps value for when calculating the WC Detail stage.\nThis replaces the 'Steps' total count before calculating the WC Detail Creativity.",
            "40", Min: 1, Max: 200, ViewMax: 100, Step: 1, Examples: ["20", "40", "60"], OrderPriority: 4, Toggleable: true, IsAdvanced: true, Group: GroupDetailOverrides, ViewType: ParamViewType.SLIDER
            ));
        DetailCFGScale = T2IParamTypes.Register<double>(new("WC Detail CFG Scale", "For the WC Detail model independently of the base model, how strongly to scale prompt input.\nHigher CFG scales tend to produce more contrast, and lower CFG scales produce less contrast.\n"
                                                                                + "Too-high values can cause corrupted/burnt images, too-low can cause nonsensical images.\n7 is a good baseline. Normal usages vary between 4 and 9.\nSome model types, such as Turbo, expect CFG around 1.",
            "7", Min: 0, Max: 100, ViewMax: 20, Step: 0.5, Examples: ["5", "6", "7", "8", "9"], OrderPriority: 5, ViewType: ParamViewType.SLIDER, Group: GroupDetailOverrides, ChangeWeight: -3, Toggleable: true, IsAdvanced: true
            ));
    }

    public static void Register(string FilePath)
    {
        PromptRegion.RegisterCustomPrefix(DIRECTIVE);
        T2IPromptHandling.PromptTagPostProcessors[DIRECTIVE] = T2IPromptHandling.PromptTagPostProcessors["segment"];
        var NodeFolder = Path.Join(FilePath, "WCNodes");
        ComfyUISelfStartBackend.CustomNodePaths.Add(NodeFolder);
        Logs.Init($"Adding {NodeFolder} to CustomNodePaths");
        AddT2IParameters();
        
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
                if (g.UserInput.TryGet(DetailModel, out T2IModel segmentModel))
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
                int growAmt = g.UserInput.Get(DetailMaskGrow, 16);
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
                    if (g.UserInput.Get(SaveDetailMask, false))
                    {
                        string imageNode = g.CreateNode("MaskToImage", new JObject()
                        {
                            ["mask"] = new JArray() { segmentNode, 0 }
                        });
                        g.CreateImageSaveNode([imageNode, 0], g.GetStableDynamicID(50000, 0));
                    }
                    int oversize = g.UserInput.Get(DetailMaskOversize, 16);
                    g.MaskShrunkInfo = CreateImageMaskCrop(g, [segmentNode, 0], g.FinalImageOut, oversize, vae, g.FinalLoadedModel, thresholdMax: g.UserInput.Get(DetailThresholdMax, 1));
                    g.EnableDifferential();
                    if (part.ContextID > 0)
                    {
                        (model, clip) = g.LoadLorasForConfinement(part.ContextID, g.FinalModel, clip);
                    }
                    JArray prompt = g.CreateConditioning(part.Prompt, clip, t2iModel, true);
                    string neg = negativeParts.FirstOrDefault(p => p.DataText == part.DataText)?.Prompt ?? negativeRegion.GlobalPrompt;
                    JArray negPrompt = g.CreateConditioning(neg, clip, t2iModel, false);
                    int steps = g.UserInput.Get(DetailSteps, g.UserInput.Get(T2IParamTypes.RefinerSteps, g.UserInput.Get(T2IParamTypes.Steps)));
                    int startStep = (int)Math.Round(steps * (1 - detailerParams.Creativity));
                    long seed = g.UserInput.Get(T2IParamTypes.Seed) + 2 + i;
                    double cfg = g.UserInput.Get(DetailCFGScale, g.UserInput.Get(T2IParamTypes.RefinerCFGScale, g.UserInput.Get(T2IParamTypes.CFGScale)));
                    string sampler = g.CreateKSampler(model, prompt, negPrompt, [g.MaskShrunkInfo.MaskedLatent, 0], cfg, steps, startStep, 10000, seed, false, true);
                    string decoded = g.CreateVAEDecode(vae, [sampler, 0]);
                    var recompositedImage = g.RecompositeCropped(g.MaskShrunkInfo.BoundsNode, [g.MaskShrunkInfo.CroppedMask, 0], g.FinalImageOut, [decoded, 0]);
                    var conditionalImage = g.CreateNode("WCSkipIfMaskEmpty", new JObject()
                    {
                        ["mask"] = new JArray() { segmentNode, 0 },
                        ["image_if_empty"] = g.FinalImageOut,
                        ["image_if_not_empty"] = recompositedImage,
                    });
                    g.FinalImageOut = [conditionalImage, 0];
                    g.MaskShrunkInfo = new(null, null, null, null);
                }
            }
        },
            // same priority as <segment>
            5);
    }
    
    public static WorkflowGenerator.ImageMaskCropData CreateImageMaskCrop(WorkflowGenerator g, JArray mask, JArray image, int growBy, JArray vae, T2IModel model, double threshold = 0.01, double thresholdMax = 1)
    {
        if (threshold > 0)
        {
            string thresholded = g.CreateNode("SwarmMaskThreshold", new JObject()
            {
                ["mask"] = mask,
                ["min"] = threshold,
                ["max"] = thresholdMax
            });
            mask = [thresholded, 0];
        }
        bool dynamicRes = g.UserInput.Get(DetailDynamicResolution, false);
        string targetRes = g.UserInput.Get(DetailTargetResolution, "0x0");
        (string targetWidth, string targetHeight) = targetRes.BeforeAndAfter('x');
        int targetX = int.Parse(targetWidth);
        int targetY = int.Parse(targetHeight);
        bool isCustomRes = targetX > 0 && targetY > 0;
        string boundsNode = g.CreateNode("WCMaskBounds", new JObject()
        {
            ["mask"] = mask,
            ["grow"] = growBy,
            ["aspect_x"] = isCustomRes ? targetX : 0,
            ["aspect_y"] = isCustomRes ? targetY : 0,
            ["dynamic"] = dynamicRes
        });
        string croppedImage = g.CreateNode("SwarmImageCrop", new JObject()
        {
            ["image"] = image,
            ["x"] = new JArray() { boundsNode, 0 },
            ["y"] = new JArray() { boundsNode, 1 },
            ["width"] = new JArray() { boundsNode, 2 },
            ["height"] = new JArray() { boundsNode, 3 }
        });
        string croppedMask = g.CreateNode("CropMask", new JObject()
        {
            ["mask"] = mask,
            ["x"] = new JArray() { boundsNode, 0 },
            ["y"] = new JArray() { boundsNode, 1 },
            ["width"] = new JArray() { boundsNode, 2 },
            ["height"] = new JArray() { boundsNode, 3 }
        });
        string scaledImage = g.CreateNode("SwarmImageScaleForMP", new JObject()
        {
            ["image"] = new JArray() { croppedImage, 0 },
            ["width"] = isCustomRes ? targetX : model?.StandardWidth <= 0 ? g.UserInput.GetImageWidth() : model.StandardWidth,
            ["height"] = isCustomRes ? targetY : model?.StandardHeight <= 0 ? g.UserInput.GetImageHeight() : model.StandardHeight,
            ["can_shrink"] = !dynamicRes
        });
        JArray encoded = g.DoMaskedVAEEncode(vae, [scaledImage, 0], [croppedMask, 0], null);
        return new(boundsNode, croppedMask, $"{encoded[0]}", scaledImage);
    }

}
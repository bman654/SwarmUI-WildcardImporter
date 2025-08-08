using System;
using System.Collections.Generic;

namespace Spoomples.Extensions.WildcardImporter;

public class DetailerParserTest
{
    public static void RunTests()
    {
        Console.WriteLine("=== Detailer Parser Test Suite ===\n");

        var testCases = new List<TestCase>
        {
            // Basic YOLO masks
            new("yolo-person", "Basic YOLO mask", 
                new YoloMask("person", "")),
            new("yolo-person[1]", "YOLO mask with object index", 
                new IndexedMask(new YoloMask("person", ""), 1)),
            new("yolo-person(face,body)", "YOLO mask with class filter", 
                new YoloMask("person", "face,body")),
            new("yolo-person(face,body)[2]", "YOLO mask with class filter and index", 
                new IndexedMask(new YoloMask("person", "face,body"), 2)),
            
            // CLIPSEG masks
            new("face", "Simple CLIPSEG mask", 
                new ClipSegMask("face")),
            new("red hair", "Multi-word CLIPSEG mask", 
                new ClipSegMask("red hair")),
            new("face[1]", "CLIPSEG mask with index", 
                new IndexedMask(new ClipSegMask("face"), 1)),
            new("red hair[3]", "Multi-word CLIPSEG mask with index", 
                new IndexedMask(new ClipSegMask("red hair"), 3)),
            
            // Threshold notation
            new("face:0.5", "CLIPSEG with threshold", 
                new ClipSegMask("face", 0.5)),
            new("face:0.5:0.8", "CLIPSEG with threshold and max", 
                new ThresholdMask(new ClipSegMask("face", 0.5), 0.8)),
            new("yolo-person:0.3", "YOLO with threshold", 
                new YoloMask("person", "", 0.3)),
            
            // Box masks
            new("box(0.1,0.2,0.3,0.4)", "Box mask", 
                new BoxMask(0.1, 0.2, 0.3, 0.4)),
            new("box(0.1,0.2,0.3,0.4)[2]", "Box mask with index", 
                new IndexedMask(new BoxMask(0.1, 0.2, 0.3, 0.4), 2)),
            
            // Invert operator
            new("!face", "Inverted mask", 
                new InvertMask(new ClipSegMask("face"))),
            new("!!face", "Double Inverted mask", 
                new ClipSegMask("face")),
            new("!(!face + 5)", "Double Inverted mask with growth", 
                new InvertMask(new GrowMask(new InvertMask(new ClipSegMask("face")), 5))),
            new("!face[1]", "Inverted indexed mask (precedence test)", 
                new InvertMask(new IndexedMask(new ClipSegMask("face"), 1))),
            
            // Grow operator
            new("face+5", "Grown mask", 
                new GrowMask(new ClipSegMask("face"), 5)),
            
            // Union operator
            new("face | hair", "Union of two masks", 
                new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair"))),
            
            // Intersect operator
            new("face & hair", "Intersection of masks", 
                new IntersectMask(new ClipSegMask("face"), new ClipSegMask("hair"))),
            
            // Bounding box function
            new("box(face)", "Bounding box of face", 
                new BoundingBoxMask(new ClipSegMask("face"))),
            
            // CRITICAL PRECEDENCE TESTS - These should reveal parser bugs
            new("face | hair + 5", "Union then grow (should be (face | hair) + 5)", 
                new GrowMask(new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")), 5)),
            
            new("face + 3 & hair", "Grow then intersect (should be (face + 3) & hair)", 
                new IntersectMask(new GrowMask(new ClipSegMask("face"), 3), new ClipSegMask("hair"))),
            
            new("!face | hair", "Invert then union (should be (!face) | hair)", 
                new UnionMask(new InvertMask(new ClipSegMask("face")), new ClipSegMask("hair"))),
            
            new("face | hair + 5 & !boy", "Complex precedence: ((face | hair) + 5) & (!boy)", 
                new IntersectMask(
                    new GrowMask(new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")), 5),
                    new InvertMask(new ClipSegMask("boy")))),
            
            // Parentheses for grouping
            new("(face | hair) + 5", "Explicit grouping with parentheses", 
                new GrowMask(new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")), 5)),
            
            new("face & (hair + 3)", "Grouping to override precedence", 
                new IntersectMask(new ClipSegMask("face"), new GrowMask(new ClipSegMask("hair"), 3))),
            
            new("!(face | hair)", "Invert grouped expression", 
                new InvertMask(new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")))),
            
            // Nested expressions
            new("box(face | hair)", "Bounding box of union", 
                new BoundingBoxMask(new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")))),
            
            new("face & box(hair)", "Intersection with bounding box", 
                new IntersectMask(new ClipSegMask("face"), new BoundingBoxMask(new ClipSegMask("hair")))),
            
            new("(face & yolo-person(classes))[2]", "Complex expression with index", 
                new IndexedMask(
                    new IntersectMask(
                        new ClipSegMask("face"), 
                        new YoloMask("person", "classes")), 
                    2)),
            
            new("(face | hair)[1]", "Union expression with index", 
                new IndexedMask(
                    new UnionMask(new ClipSegMask("face"), new ClipSegMask("hair")), 
                    1)),
            
            new("box(face)[3]", "Bounding box with index", 
                new IndexedMask(new BoundingBoxMask(new ClipSegMask("face")), 3)),
            
            new("(face + 5)[2]", "Grown mask with index", 
                new IndexedMask(new GrowMask(new ClipSegMask("face"), 5), 2)),
            
            new("face[1] | hair[2]", "Index operators in union", 
                new UnionMask(
                    new IndexedMask(new ClipSegMask("face"), 1), 
                    new IndexedMask(new ClipSegMask("hair"), 2))),
            
            new("face[1] + 3", "Index then grow (should be (face[1]) + 3)", 
                new GrowMask(new IndexedMask(new ClipSegMask("face"), 1), 3)),
            
            new("!yolo-person(face,body)[1]", "Invert indexed YOLO (should be !(yolo-person(face,body)[1]))", 
                new InvertMask(new IndexedMask(new YoloMask("person", "face,body"), 1))),
        };

        int passed = 0;
        int total = testCases.Count;

        foreach (var testCase in testCases)
        {
            Console.WriteLine($"Testing: {testCase.Input}");
            Console.WriteLine($"Description: {testCase.Description}");
            
            try
            {
                var actual = Detailer.ParseMaskSpecifier(testCase.Input);
                
                if (ASTEquals(actual, testCase.Expected))
                {
                    Console.WriteLine($"✓ SUCCESS: AST matches expected structure");
                    Console.WriteLine($"  Expected: {FormatAST(testCase.Expected)}");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"✗ FAILED: AST structure mismatch");
                    Console.WriteLine($"  Expected: {FormatAST(testCase.Expected)}");
                    Console.WriteLine($"  Actual:   {FormatAST(actual)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ FAILED: Parse error - {ex.Message}");
                Console.WriteLine($"  Expected: {FormatAST(testCase.Expected)}");
            }
            
            Console.WriteLine();
        }

        Console.WriteLine($"=== Test Results ===");
        Console.WriteLine($"Passed: {passed}/{total}");
        Console.WriteLine($"Success Rate: {(double)passed / total * 100:F1}%");

        if (passed == total)
        {
            Console.WriteLine("🎉 All tests passed!");
        }
        else
        {
            Console.WriteLine($"❌ {total - passed} tests failed.");
            Console.WriteLine("Parser has bugs that need to be fixed!");
        }
    }

    private record TestCase(string Input, string Description, MaskSpecifier Expected);

    private static bool ASTEquals(MaskSpecifier actual, MaskSpecifier expected)
    {
        return (actual, expected) switch
        {
            (YoloMask a, YoloMask e) => a.ModelName == e.ModelName && a.ClassFilter == e.ClassFilter,
            (ClipSegMask a, ClipSegMask e) => a.Text == e.Text,
            (ThresholdMask a, ThresholdMask e) => ASTEquals(a.BaseMask, e.BaseMask) && 
                                                 a.ThresholdMax == e.ThresholdMax,
            (BoxMask a, BoxMask e) => Math.Abs(a.X - e.X) < 0.001 && Math.Abs(a.Y - e.Y) < 0.001 && 
                                     Math.Abs(a.Width - e.Width) < 0.001 && Math.Abs(a.Height - e.Height) < 0.001,
            (InvertMask a, InvertMask e) => ASTEquals(a.Mask, e.Mask),
            (GrowMask a, GrowMask e) => ASTEquals(a.Mask, e.Mask) && a.Pixels == e.Pixels,
            (UnionMask a, UnionMask e) => ASTEquals(a.Left, e.Left) && ASTEquals(a.Right, e.Right),
            (IntersectMask a, IntersectMask e) => ASTEquals(a.Left, e.Left) && ASTEquals(a.Right, e.Right),
            (BoundingBoxMask a, BoundingBoxMask e) => ASTEquals(a.Mask, e.Mask),
            (IndexedMask a, IndexedMask e) => ASTEquals(a.Mask, e.Mask) && a.Index == e.Index,
            _ => false
        };
    }

    private static string FormatAST(MaskSpecifier mask, int indent = 0)
    {
        var prefix = new string(' ', indent * 2);
        
        return mask switch
        {
            YoloMask yolo => $"{prefix}YoloMask(ModelName: '{yolo.ModelName}', ClassFilter: {yolo.ClassFilter})",
            ClipSegMask clip => $"{prefix}ClipSegMask(Text: '{clip.Text}')",
            ThresholdMask thresh => $"{prefix}ThresholdMask(\n{FormatAST(thresh.BaseMask, indent + 1)},\n{prefix}  ThresholdMax: {thresh.ThresholdMax})",
            BoxMask box => $"{prefix}BoxMask(X: {box.X}, Y: {box.Y}, Width: {box.Width}, Height: {box.Height})",
            InvertMask invert => $"{prefix}InvertMask(\n{FormatAST(invert.Mask, indent + 1)})",
            GrowMask grow => $"{prefix}GrowMask(\n{FormatAST(grow.Mask, indent + 1)},\n{prefix}  Pixels: {grow.Pixels})",
            UnionMask union => $"{prefix}UnionMask(\n{FormatAST(union.Left, indent + 1)},\n{FormatAST(union.Right, indent + 1)})",
            IntersectMask intersect => $"{prefix}IntersectMask(\n{FormatAST(intersect.Left, indent + 1)},\n{FormatAST(intersect.Right, indent + 1)})",
            BoundingBoxMask bbox => $"{prefix}BoundingBoxMask(\n{FormatAST(bbox.Mask, indent + 1)})",
            IndexedMask indexed => $"{prefix}IndexedMask(\n{FormatAST(indexed.Mask, indent + 1)},\n{prefix}  Index: {indexed.Index})",
            _ => $"{prefix}Unknown mask type: {mask.GetType().Name}"
        };
    }
}

using System.Text.Json;
using TwelveJade.Presentation;

if (args.Length != 1) throw new ArgumentException("Pass the JSON produced by measure_assets.py");
using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
using var approvedWalks = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "approved-walk-bounds.json")));
int checks = 0, layouts = 0, walkFrames = 0;
float oldMinGrowth = float.MaxValue, oldMaxGrowth = 0;
void Near(float actual, float expected, string context, float tolerance = .0001f)
{
    checks++;
    if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > tolerance)
        throw new Exception($"{context}: expected {expected}, got {actual}");
}
float Number(JsonElement value, string field) => value.GetProperty(field).GetSingle();
foreach (var hero in document.RootElement.GetProperty("heroes").EnumerateArray())
{
    var standing = hero.GetProperty("standing");
    var source = standing.GetProperty("frames")[0];
    var standingName = standing.GetProperty("name").GetString();
    float standHeight = Number(standing, "height"), standTop = Number(source, "top"), standBottom = Number(source, "bottom");
    var registered = ActionPoseGeometry.StandingBounds(standingName);
    Near(registered.Top, standTop, standingName + " measured top");
    Near(registered.Bottom, standBottom, standingName + " measured sole");
    Near(registered.CanvasHeight, standHeight, standingName + " canvas");
    var strikes = hero.GetProperty("strikes");
    string strikeName = strikes.GetProperty("name").GetString();
    for (int frame = 0; frame < 3; frame++)
    {
        var attack = strikes.GetProperty("frames")[frame];
        var registeredAttack = ActionPoseGeometry.StrikeBounds(strikeName, frame);
        float attackHeight = Number(strikes, "height"), attackTop = Number(attack, "top"), attackBottom = Number(attack, "bottom");
        Near(registeredAttack.Top, attackTop, strikeName + " measured top " + frame);
        Near(registeredAttack.Bottom, attackBottom, strikeName + " measured sole " + frame);
        Near(registeredAttack.CanvasHeight, attackHeight, strikeName + " canvas " + frame);
        float oldGrowth = (attackBottom - attackTop) / 450f / ((standBottom - standTop) / standHeight) - 1f;
        oldMinGrowth = MathF.Min(oldMinGrowth, oldGrowth); oldMaxGrowth = MathF.Max(oldMaxGrowth, oldGrowth);
        foreach (float rectangle in new[] { 170f, 336f })
        foreach (float tier in new[] { .8f, 1f, 1.25f })
        {
            var layout = ActionPoseGeometry.Strike(rectangle, standingName, strikeName, frame);
            float expectedBody = rectangle * (standBottom - standTop) / standHeight * tier;
            float expectedFoot = rectangle * (standHeight - standBottom) / standHeight * tier;
            float actualBody = layout.Height * (attackBottom - attackTop) / attackHeight * tier;
            float actualFoot = layout.Height * ((attackHeight - attackBottom) / attackHeight - layout.PivotY) * tier;
            Near(actualBody, expectedBody, standingName + " attack visible height " + frame, .0002f);
            Near(actualFoot, expectedFoot, standingName + " attack visible sole " + frame, .0002f);
            layouts++;
        }
    }
    // Each approved walk pose has its own authored knee bend and head height. Check its
    // exact alpha registration so replacement art cannot silently pass by widening a tolerance.
    // All poses retain the same planted sole; body rise/fall is limited to 3% of standing height.
    var walking = hero.GetProperty("walking");
    string walkName = walking.GetProperty("name").GetString();
    var approved = approvedWalks.RootElement.GetProperty(walkName);
    Near(Number(walking, "height"), Number(approved, "height"), walkName + " approved canvas");
    var frames = walking.GetProperty("frames");
    Near(frames.GetArrayLength(), 8, walkName + " eight-frame cycle");
    Near(approved.GetProperty("tops").GetArrayLength(), frames.GetArrayLength(), walkName + " registered poses");
    Near(approved.GetProperty("bottoms").GetArrayLength(), frames.GetArrayLength(), walkName + " registered soles");
    float standingBody = (standBottom - standTop) / standHeight * 170f;
    for (int i = 0; i < frames.GetArrayLength(); i++)
    {
        var frame = frames[i];
        Near(Number(frame, "top"), approved.GetProperty("tops")[i].GetSingle(), walkName + " approved pose top " + i);
        Near(Number(frame, "bottom"), approved.GetProperty("bottoms")[i].GetSingle(), walkName + " approved pose sole " + i);
        float height = (Number(frame, "bottom") - Number(frame, "top")) / Number(walking, "height") * 170f;
        float sole = (Number(walking, "height") - Number(frame, "bottom")) / Number(walking, "height") * 170f;
        checks++;
        if (MathF.Abs(height - standingBody) > standingBody * .03f)
            throw new Exception(walkName + ": authored weight shift exceeds 3% of standing height");
        Near(sole, (standHeight - standBottom) / standHeight * 170f, walkName + " planted sole " + i, .7f);
        walkFrames++;
    }
}
var fallback = ActionPoseGeometry.Strike(170, "unregistered-stand", "unregistered-strike", 0);
Near(fallback.Height, 217.6f, "legacy unregistered rectangle");
Near(fallback.PivotY, 28f / 576f, "legacy unregistered foot");
var zero = ActionPoseGeometry.Strike(0, "hero-male-front", "combat-hero-male-strikes", 0);
Near(zero.Height, 0, "zero height");
Near(zero.PivotY, 27f / 576f, "zero-height finite pivot");
Console.WriteLine($"PASS: {checks} assertions; {layouts} asset-derived layouts; 6 stands / {walkFrames} walks / 6 strikes.");
Console.WriteLine($"Old default-tier visible-height growth: {oldMinGrowth:P2} to {oldMaxGrowth:P2}; corrected height/sole error < 0.0002 units.");
Console.WriteLine("Unity transform scale, mirror, reduced motion, lifecycle and rendering are covered separately by TwelveJade.Editor.ActionPoseSizeChecks.Run.");

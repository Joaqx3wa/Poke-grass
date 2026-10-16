using System.Drawing;
using System.IO;

namespace PokeGrass;

internal static class CompanionBehaviorSelfTest
{
    internal static string Summary { get; private set; } = string.Empty;

    internal static void Run()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets", "ui", "pokemon_reactions");
        foreach (var emotion in Enum.GetValues<CompanionEmotion>())
        {
            var path = Path.Combine(assets, $"{emotion.ToString().ToLowerInvariant()}.png");
            Assert(File.Exists(path), $"Falta el recurso de reacción {emotion}.");
            using var image = new Bitmap(path);
            Assert(image.Width == 32 && image.Height == 32,
                $"El recurso {emotion} debe conservar sus dimensiones originales de 32×32.");
            Assert(image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                $"El recurso {emotion} debe conservar alfa real.");
            Assert(image.GetPixel(0, 0).A == 0 && image.GetPixel(16, 16).A > 0,
                $"El recurso {emotion} debe tener transparencia y contenido visible.");
        }

        var toyPath = Path.Combine(AppContext.BaseDirectory, "assets", "items", "toy.png");
        Assert(File.Exists(toyPath), "Falta el sprite físico del juguete.");
        using (var toy = new Bitmap(toyPath))
        {
            Assert(toy.Width == 16 && toy.Height == 16,
                "El sprite del juguete debe conservar sus dimensiones originales de 16×16.");
            Assert(toy.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb
                && toy.GetPixel(0, 0).A == 0 && toy.GetPixel(8, 8).A > 0,
                "El juguete debe conservar transparencia alfa y contenido visible.");
        }

        // The same exponential drag used by the live toy timer must settle a
        // normal nudge in less than one sprite width, and collisions must not
        // keep bouncing between the same two bodies.
        var toySpeed = DesktopToyWindow.PushSpeedPixelsPerSecond;
        var rolledDistance = 0d;
        var rollTicks = 0;
        const double frameSeconds = 1d / 60d;
        while (toySpeed >= 2d && rollTicks < 600)
        {
            rolledDistance += toySpeed * frameSeconds;
            toySpeed *= DesktopToyWindow.GetRollingFriction(frameSeconds);
            rollTicks++;
        }
        Assert(rolledDistance is > 0d and < 32d && rollTicks < 600,
            "Un empujón debe rodar una distancia corta y detenerse gradualmente.");
        var collisionSpeed = DesktopToyWindow.PushSpeedPixelsPerSecond;
        for (var impact = 0; impact < 2; impact++) collisionSpeed *= DesktopToyWindow.CollisionDamping;
        Assert(collisionSpeed < 2d,
            "El contacto con un Pokémon debe amortiguar rápidamente el movimiento, sin vibración repetida.");
        var soloSpeed = DesktopToyWindow.SoloPlayPushSpeedPixelsPerSecond;
        var soloDistance = 0d;
        var soloTicks = 0;
        while (soloSpeed >= 2d && soloTicks < 600)
        {
            soloDistance += soloSpeed * frameSeconds;
            soloSpeed *= DesktopToyWindow.GetRollingFriction(frameSeconds,
                DesktopToyWindow.SoloPlayFrictionPerSecond);
            soloTicks++;
        }
        Assert(soloDistance is > 220d and < 330d && soloTicks < 600,
            "En modo individual, la pelota debe rodar más lejos y detenerse gradualmente para que el Pokémon la persiga.");

        // The full sprite canvases overlap here, but the visible body bounds do not.
        var first = CompanionCollisionGeometry.ToScreenRect(100, 100, 10, 50, new System.Windows.Int32Rect(20, 30, 32, 28), 1.5);
        var second = CompanionCollisionGeometry.ToScreenRect(150, 100, 10, 50, new System.Windows.Int32Rect(20, 30, 32, 28), 1.5);
        Assert(first.X == 145 && first.Y == 220 && first.Width == 48 && first.Height == 42,
            "Las colisiones deben utilizar el recuadro alfa visible y respetar el DPI.");
        Assert(!CompanionCollisionGeometry.Overlaps(first, second),
            "Los márgenes transparentes de los spritesheets no deben provocar colisiones.");
        Assert(CompanionCollisionGeometry.IsNear(first, second, 80)
            && !CompanionCollisionGeometry.IsNear(first, second, 30),
            "La detección de proximidad debe poder iniciar encuentros sin esperar una colisión.");
        Assert(CompanionCollisionGeometry.Overlaps(first,
                CompanionCollisionGeometry.ToScreenRect(145, 100, 10, 50, new System.Windows.Int32Rect(20, 30, 32, 28), 1.5)),
            "Los cuerpos alfa que sí se cruzan deben activar el separador.");

        var upward = new System.Windows.Point(0, -1);
        var clampedAtTop = CompanionCollisionGeometry.ClampAndReflect(20, -2, 64, 80,
            new Rectangle(0, 0, 500, 400), ref upward);
        Assert(clampedAtTop.Y == 0 && upward.Y == 1,
            "Al alcanzar el borde superior, el acompañante debe girar hacia abajo y no seguir chocando contra el límite.");
        var downward = new System.Windows.Point(0, 1);
        var clampedAtBottom = CompanionCollisionGeometry.ClampAndReflect(20, 330, 64, 80,
            new Rectangle(0, 0, 500, 400), ref downward);
        Assert(clampedAtBottom.Y == 320 && downward.Y == -1,
            "Al alcanzar el borde inferior, el acompañante debe girar hacia arriba.");

        Summary = $"Acompañantes: ocho reacciones PNG 32×32 y juguete PNG 16×16 con alfa verificados; geometría de proximidad y colisión validada; empuje normal {rolledDistance:0.0}px y juego individual {soloDistance:0.0}px, ambos se detienen en la simulación.";
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

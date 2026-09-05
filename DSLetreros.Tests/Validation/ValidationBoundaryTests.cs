using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.Validation;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DSLetreros.Tests.Validation;

/// <summary>Frontera defensiva del documento: límites y validaciones nuevas del ProjectValidator.</summary>
public class ProjectValidatorBoundaryTests
{
    private static Project NewValidProject()
    {
        var p = new Project { Name = "Test", Canvas = new CanvasDefinition(32, 16), FormatVersion = 1 };
        var scene = new Scene { Name = "Escena 1", Duration = TimeSpan.FromSeconds(5) };
        var layer = new Layer { Name = "Capa 1", Order = 0 };
        layer.Objects.Add(new TextObject { Name = "Texto", Text = "HOLA" });
        scene.Layers.Add(layer);
        p.Scenes.Add(scene);
        return p;
    }

    [Fact]
    public void Canvas_over_max_dimension_fails()
    {
        var p = NewValidProject();
        p.Canvas = new CanvasDefinition(600, 16); // > 512
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Unsupported_format_version_fails(int version)
    {
        var p = NewValidProject();
        p.FormatVersion = version;
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Duplicate_layer_id_fails()
    {
        var p = NewValidProject();
        var scene = p.Scenes[0];
        var dupLayer = new Layer { Name = "Capa 2", Id = scene.Layers[0].Id, Order = 1 };
        scene.Layers.Add(dupLayer);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Overlong_object_name_fails()
    {
        var p = NewValidProject();
        p.Scenes[0].Layers[0].Objects[0].Name = new string('x', ProjectValidator.MaxNameLength + 1);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Negative_position_fails()
    {
        var p = NewValidProject();
        p.Scenes[0].Layers[0].Objects[0].Position = new PixelPoint(-1, 0);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Drawing_pixeldata_length_mismatch_fails()
    {
        var p = NewValidProject();
        var drawing = new DrawingObject
        {
            Name = "Dib",
            Size = new PixelSize(2, 2),
            BitsPerPixel = 1,
            Palette = new() { RgbColor.Red },
            PixelData = new byte[] { 0, 1, 1 }, // sólo 3 bytes, se esperan 4
        };
        p.Scenes[0].Layers[0].Objects.Add(drawing);
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Monochrome_drawing_with_bit_mask_is_valid()
    {
        // Contrato existente: DrawingObject es máscara de bits (byte=1 con paleta de 1 color es válido).
        var p = NewValidProject();
        var drawing = new DrawingObject
        {
            Name = "Corazón",
            Size = new PixelSize(2, 2),
            BitsPerPixel = 1,
            Palette = new() { RgbColor.Red },
            PixelData = new byte[] { 0, 1, 1, 1 },
        };
        p.Scenes[0].Layers[0].Objects.Add(drawing);
        var r = ProjectValidator.Validate(p);
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    [Fact]
    public void Embedded_asset_with_out_of_range_index_fails()
    {
        var p = NewValidProject();
        // Asset indexado (ícono): 2 colores, índice 5 fuera de paleta.
        p.EmbeddedAssets["A"] = "{\"width\":1,\"height\":1,\"pixels\":\"" +
            Convert.ToBase64String(new byte[] { 5 }) + "\",\"palette\":[{\"r\":0,\"g\":0,\"b\":0},{\"r\":255,\"g\":0,\"b\":0}]}";
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Embedded_asset_with_invalid_json_fails()
    {
        var p = NewValidProject();
        p.EmbeddedAssets["A"] = "{no es json";
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Scene_objects_exceeding_max_fails()
    {
        // Escena con > MaxObjectsPerScene objetos.
        var p = NewValidProject();
        var layer = p.Scenes[0].Layers[0];
        for (int i = 0; i < ProjectValidator.MaxObjectsPerScene; i++)
            layer.Objects.Add(new TextObject { Name = $"t{i}", Text = "x" });
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }

    [Fact]
    public void Non_finite_scene_duration_fails()
    {
        var p = NewValidProject();
        p.Scenes[0].Duration = TimeSpan.MaxValue;
        Assert.False(ProjectValidator.Validate(p).IsValid);
    }
}

/// <summary>Aplicación real de MaxObjectsPerScene en EditingService.</summary>
public class EditingServiceLimitTests
{
    private static Scene NewScene()
    {
        var s = new Scene { Name = "S", Duration = TimeSpan.FromSeconds(5) };
        s.Layers.Add(new Layer { Name = "L", Order = 0 });
        return s;
    }

    [Fact]
    public void AddText_throws_when_capacity_exceeded()
    {
        var svc = new EditingService();
        var scene = NewScene();
        for (int i = 0; i < EditingService.MaxObjectsPerScene; i++)
            svc.AddText(scene, $"t{i}", new PixelPoint(0, 0));

        Assert.Throws<InvalidOperationException>(
            () => svc.AddText(scene, "overflow", new PixelPoint(0, 0)));
    }

    [Fact]
    public void Duplicate_checks_total_before_inserting()
    {
        var svc = new EditingService();
        var scene = NewScene();
        // 999 objetos existentes + duplicar 2 => 1001 > 1000 => debe lanzar sin insertar.
        for (int i = 0; i < EditingService.MaxObjectsPerScene - 1; i++)
            svc.AddText(scene, $"t{i}", new PixelPoint(0, 0));

        var toDup = new[] { scene.Layers[0].Objects[0], scene.Layers[0].Objects[1] };
        int before = scene.Layers[0].Objects.Count;
        Assert.Throws<InvalidOperationException>(() => svc.DuplicateObjects(scene, toDup));
        Assert.Equal(before, scene.Layers[0].Objects.Count); // sin cambios
    }

    [Fact]
    public void AddDrawing_rejects_overflowing_size()
    {
        var svc = new EditingService();
        var scene = NewScene();
        // int.MaxValue * 2 => overflow checked
        Assert.Throws<ArgumentOutOfRangeException>(
            () => svc.AddDrawing(scene, new PixelSize(int.MaxValue, 2)));
    }

    [Fact]
    public void AddDrawing_rejects_oversized_pixel_count()
    {
        var svc = new EditingService();
        var scene = NewScene();
        // 1024x1024 = 1M > 512*512 max
        Assert.Throws<ArgumentOutOfRangeException>(
            () => svc.AddDrawing(scene, new PixelSize(1024, 1024)));
    }
}

/// <summary>Overflow y entradas extremas en ImageRasterizer.</summary>
public class ImageRasterizerBoundaryTests
{
    [Fact]
    public void Rasterize_rejects_overflowing_source_dimensions()
    {
        // int.MaxValue * int.MaxValue desborda; el long checked lo captura.
        var result = ImageRasterizer.Rasterize(Array.Empty<byte>(), int.MaxValue, int.MaxValue, 1, 1);
        Assert.False(result.Success);
    }

    [Fact]
    public void Rasterize_rejects_zero_target_width()
    {
        var rgba = new byte[4 * 4 * 4];
        var result = ImageRasterizer.Rasterize(rgba, 4, 4, 0, 4);
        Assert.False(result.Success);
    }

    [Fact]
    public void Rasterize_rejects_huge_source_width()
    {
        // srcWidth enorme => píxeles de origen > MaxSourcePixels.
        var result = ImageRasterizer.Rasterize(Array.Empty<byte>(), 100_000, 100_000, 1, 1);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    [InlineData(-1)]
    public void Rasterize_rejects_invalid_max_colors(int maxColors)
    {
        var rgba = new byte[4 * 4 * 4];
        var result = ImageRasterizer.Rasterize(rgba, 4, 4, 4, 4, maxColors: maxColors);
        Assert.False(result.Success);
    }
}

/// <summary>Límites y escritura atómica en LibraryService.</summary>
public class LibraryServiceBoundsTests : IDisposable
{
    private readonly string _root;
    private readonly LibraryService _svc;

    public LibraryServiceBoundsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dsletras-lib-bounds-" + Guid.NewGuid().ToString("N"));
        _svc = new LibraryService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Save_rejects_overflowing_dimensions()
    {
        byte[] pixels = Array.Empty<byte>();
        var (ok, _, _) = _svc.SaveCustomDrawing("X", int.MaxValue, 2, pixels);
        Assert.False(ok);
    }

    [Fact]
    public void Save_rejects_dimension_over_max()
    {
        var (ok, _, _) = _svc.SaveCustomDrawing("X", 1024, 2, new byte[1]);
        Assert.False(ok);
    }

    [Fact]
    public void Save_rejects_empty_palette()
    {
        var (ok, _, _) = _svc.SaveCustomDrawing("X", 2, 2, new byte[] { 0, 1, 0, 1 }, new());
        Assert.False(ok);
    }

    [Fact]
    public void Save_does_not_leave_tmp_file_on_success()
    {
        var (ok, _, _) = _svc.SaveCustomDrawing("X", 2, 2, new byte[] { 0, 1, 1, 0 });
        Assert.True(ok);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public void List_ignores_tmp_files()
    {
        // Simula un temporal residual: no debe aparecer como dibujo.
        File.WriteAllText(Path.Combine(_root, "deadbeef.json.tmp"), "...");
        Assert.Empty(_svc.ListDrawings());
    }
}

/// <summary>Redacción de secretos en mensaje, excepción y estado estructurado + rotación por tamaño.</summary>
public class RollingFileLoggerTests : IDisposable
{
    private readonly string _dir;
    private readonly RollingFileLogger _logger;

    public RollingFileLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dsletras-log-" + Guid.NewGuid().ToString("N"));
        _logger = new RollingFileLogger("Test", _dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string[] ReadLines() =>
        Directory.EnumerateFiles(_dir, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();

    [Fact]
    public void Redacts_secret_in_message()
    {
        _logger.Log(LogLevel.Information, 0, "state",
            null, (s, e) => "user=alice password=supersecret token=abc123");
        var line = ReadLines().Single();
        Assert.DoesNotContain("supersecret", line);
        Assert.DoesNotContain("abc123", line);
        Assert.Contains("[REDACTADO]", line);
    }

    [Fact]
    public void Redacts_secret_in_exception_message()
    {
        var ex = new InvalidOperationException("falló con authorization=Bearer-TOKEN-VALUE y api_key=KEY123");
        _logger.Log(LogLevel.Error, 0, "state", ex, (s, e) => "boom");
        var line = ReadLines().Single();
        Assert.DoesNotContain("TOKEN-VALUE", line);
        Assert.DoesNotContain("KEY123", line);
        Assert.DoesNotContain("Bearer", line);
        Assert.Contains("InvalidOperationException", line);
    }

    [Fact]
    public void Redacts_cookie_and_password_in_message_and_exception()
    {
        var ex = new Exception("cookie=SESSIONID=secretcookie; password=hunter2");
        _logger.Log(LogLevel.Warning, 0, "state", ex,
            (s, e) => "cookie=nebula-session=xyz password=admin123");
        var line = ReadLines().Single();
        foreach (var secret in new[] { "secretcookie", "hunter2", "nebula-session", "admin123", "xyz" })
            Assert.DoesNotContain(secret, line);
    }

    [Fact]
    public void Redacts_structured_state()
    {
        // El estado estructurado (formato clave/valor) también se redacta.
        _logger.Log(LogLevel.Information, 0,
            new object[] { "authorization", "Bearer abc", "api_key", "zzz" },
            null, (s, e) => "msg");
        var line = ReadLines().Single();
        Assert.DoesNotContain("abc", line);
        Assert.DoesNotContain("zzz", line);
    }

    // ----- IsEnabled: branch `logLevel != None` -----

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void IsEnabled_is_true_for_all_levels_except_none(LogLevel level)
    {
        Assert.True(_logger.IsEnabled(level));
    }

    [Fact]
    public void IsEnabled_is_false_for_none()
    {
        Assert.False(_logger.IsEnabled(LogLevel.None));
    }

    [Fact]
    public void Log_with_none_level_writes_nothing()
    {
        _logger.Log(LogLevel.None, 0, "state", null, (s, e) => "should not appear");
        Assert.Empty(ReadLines());
    }
}
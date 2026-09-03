using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

/// <summary>Biblioteca e importación (iconos + imágenes rasterizadas).</summary>
public partial class LibraryController : Controller
{
    private readonly LibraryService _library;

    public LibraryController(LibraryService library) => _library = library;

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Drawings()
    {
        return Json(new { success = true, drawings = _library.ListDrawings() });
    }

    [HttpPost]
    public IActionResult SaveDrawing([FromBody] SaveDrawingRequest request)
    {
        byte[] pixels = (request.Pixels ?? Array.Empty<int>()).Select(i => (byte)(i & 0xFF)).ToArray();
        var (ok, message, id) = _library.SaveCustomDrawing(
            request.Name ?? "Dibujo", request.Width, request.Height, pixels,
            request.Palette?.Select(c => new Domain.ValueObjects.RgbColor(c.R, c.G, c.B)).ToList());
        return Json(new { success = ok, message, id = id?.Value.ToString("N") });
    }

    [HttpPost]
    public IActionResult DeleteDrawing([FromBody] DeleteDrawingRequest request)
    {
        if (!Guid.TryParse(request.Id, out var guid))
            return Json(new { success = false, message = "ID inválido." });
        var ok = _library.DeleteDrawing(new Domain.ValueObjects.AssetId(guid));
        return Json(new { success = ok, message = ok ? "Eliminado." : "No encontrado." });
    }

    [HttpGet]
    public IActionResult Icons()
    {
        var icons = BuiltInIcons.All().Select(i => new
        {
            id = i.Id.Value.ToString("N"),
            name = i.Name,
            category = i.Category,
            width = i.Width,
            height = i.Height,
            pixels = i.Pixels,
            palette = i.Palette.Select(p => new { r = p.R, g = p.G, b = p.B }),
        });
        return Json(new { success = true, icons });
    }

    /// <summary>Importa una imagen rasterizada (RGBA) y devuelve un asset embebible.</summary>
    [HttpPost]
    public IActionResult RasterizeImage([FromBody] RasterizeRequest request)
    {
        if (request.Rgba == null || request.SrcWidth <= 0 || request.SrcHeight <= 0)
            return Json(new { success = false, message = "Imagen inválida." });

        byte[] rgba = request.Rgba.Select(i => (byte)(i & 0xFF)).ToArray();

        int tw = request.TargetWidth > 0 ? request.TargetWidth : request.SrcWidth;
        int th = request.TargetHeight > 0 ? request.TargetHeight : request.SrcHeight;

        var result = ImageRasterizer.Rasterize(
            rgba, request.SrcWidth, request.SrcHeight,
            tw, th, request.Dither != false, request.MaxColors > 0 ? request.MaxColors : 16);

        if (!result.Success)
            return Json(new { success = false, message = result.Message });

        var asset = new ImageAsset
        {
            Name = request.Name ?? "Imagen",
            SourceFormat = request.Format ?? "PNG",
            Width = result.Width,
            Height = result.Height,
            Pixels = result.Indices,
            Palette = result.Palette,
            ConversionMetadata = $"nearest-neighbor;dither={request.Dither != false};colors={result.Palette.Count}",
            License = new AssetLicenseInfo { Origin = "importado por usuario", License = "desconocida" },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(asset, AtlasJson.Options);
        return Json(new { success = true, assetId = asset.Id.Value.ToString("N"), assetJson = json });
    }
}

public sealed class RasterizeRequest
{
    public string? Name { get; set; }
    public string? Format { get; set; }
    public int SrcWidth { get; set; }
    public int SrcHeight { get; set; }
    public int TargetWidth { get; set; }
    public int TargetHeight { get; set; }
    public int[]? Rgba { get; set; }
    public bool Dither { get; set; } = true;
    public int MaxColors { get; set; } = 16;
}

public sealed class SaveDrawingRequest
{
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int[]? Pixels { get; set; }
    public List<RgbColorDto>? Palette { get; set; }
}

public sealed class RgbColorDto
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}

public sealed class DeleteDrawingRequest
{
    public string Id { get; set; } = string.Empty;
}
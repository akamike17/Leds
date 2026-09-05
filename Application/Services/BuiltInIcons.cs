using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;

namespace DSLetreros.Application.Services;

/// <summary>Catálogo de iconos locales embebidos (estilo Pixelarticons, origen local, sin CDN).</summary>
public static class BuiltInIcons
{
    /// <summary>Iconos 8x8 monocromos (1 = píxel encendido). Categorías por nombre.</summary>
    public static IReadOnlyList<IconAsset> All() => Catalog;

    private static readonly List<IconAsset> Catalog = Build();

    private static List<IconAsset> Build()
    {
        var icons = new List<IconAsset>
        {
            Make("heart", "Corazón", "Corazones",
                new[] { "Corazón", "Heart", "amor", "love" }),
            Make("star", "Estrella", "Símbolos",
                new[] { "Estrella", "Star", "favorito" }),
            Make("arrow-right", "Flecha derecha", "Flechas",
                new[] { "Flecha", "Arrow", "derecha" }),
            Make("arrow-left", "Flecha izquierda", "Flechas",
                new[] { "Flecha", "Arrow", "izquierda" }),
            Make("arrow-up", "Flecha arriba", "Flechas",
                new[] { "Flecha", "Arrow", "arriba" }),
            Make("arrow-down", "Flecha abajo", "Flechas",
                new[] { "Flecha", "Arrow", "abajo" }),
            Make("check", "Comprobado", "Símbolos",
                new[] { "Check", "comprobado", "ok" }),
            Make("cross", "Cruz", "Símbolos",
                new[] { "Cross", "cerrar", "x" }),
            Make("cart", "Carrito", "Comercio",
                new[] { "Carrito", "Cart", "compra" }),
            Make("phone", "Teléfono", "Comunicación",
                new[] { "Teléfono", "Phone", "llamar" }),
            Make("bolt", "Rayo", "Tecnología",
                new[] { "Rayo", "Bolt", "energía" }),
            Make("gear", "Engranaje", "Herramientas",
                new[] { "Engranaje", "Gear", "ajustes" }),
            Make("wifi", "Wi-Fi", "Tecnología",
                new[] { "Wifi", "wi-fi" }),
            Make("clock", "Reloj", "Tiempo",
                new[] { "Reloj", "Clock", "tiempo" }),
            Make("car", "Coche", "Transporte",
                new[] { "Coche", "Car", "auto" }),
            Make("person", "Persona", "Personas",
                new[] { "Persona", "Person", "usuario" }),
            Make("wrench", "Herramienta", "Herramientas",
                new[] { "Herramienta", "Wrench", "tool", "llave" }),
            Make("computer", "Computadora", "Computación",
                new[] { "Computadora", "Computer", "pc", "ordenador" }),
            Make("food", "Comida", "Comida",
                new[] { "Comida", "Food", "comer", "burger" }),
            Make("music", "Música", "Audio",
                new[] { "Música", "Music", "nota", "audio" }),
            Make("dollar", "Dólar", "Dinero",
                new[] { "Dólar", "Dollar", "dinero", "$", "money" }),
        };
        return icons;
    }

    private static IconAsset Make(string id, string name, string category, string[] aliases)
    {
        var (w, h, bits) = IconDefs.Get(id);
        // monocromo: índice 0 = transparente, 1 = encendido (blanco)
        var palette = new List<RgbColor> { RgbColor.Black, RgbColor.White };
        var pixels = new byte[w * h];
        for (int i = 0; i < w * h; i++) pixels[i] = bits.Contains(i) ? (byte)1 : (byte)0;
        return new IconAsset
        {
            Id = AssetId.New(),
            Name = name,
            Category = category,
            Aliases = aliases.ToList(),
            Tags = aliases.ToList(),
            Width = w,
            Height = h,
            Pixels = pixels,
            Palette = palette,
            TransparentIndex = 0,   // índice 0 = fondo transparente (no borra lo de debajo)
            License = new AssetLicenseInfo { Origin = "DSLetras built-in", License = "propio (original)", SourceUrl = string.Empty },
        };
    }
}

/// <summary>Definiciones bitmap de los iconos (coordenadas de píxeles encendidos).</summary>
internal static class IconDefs
{
    private static readonly Dictionary<string, (int w, int h, int[] bits)> Defs = new()
    {
        ["heart"] = (8, 8, new[] {
            10, 11, 17, 20, 26, 27, 29, 33, 34, 35, 36, 42, 43, 44, 51, 52, 61 }),
        ["star"] = (8, 8, new[] {
            27, 34, 35, 36, 37, 42, 44, 50, 51, 52, 59, 60, 61 }),
        ["arrow-right"] = (8, 8, new[] {
            24, 33, 40, 41, 42, 44, 48, 54 }),
        ["arrow-left"] = (8, 8, new[] {
            27, 22, 13, 12, 14, 8, 6 }),
        ["arrow-up"] = (8, 8, new[] {
            19, 26, 27, 28, 33, 35, 41, 49 }),
        ["arrow-down"] = (8, 8, new[] {
            13, 21, 29, 30, 31, 35, 37, 45 }),
        ["check"] = (8, 8, new[] {
            9, 18, 27, 36, 46, 55 }),
        ["cross"] = (8, 8, new[] {
            9, 18, 27, 36, 45, 54, 63 }),
        ["cart"] = (8, 8, new[] {
            1, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 22, 24, 31, 32, 33, 35, 36, 37, 39, 40, 47, 48 }),
        ["phone"] = (8, 8, new[] {
            10, 11, 12, 13, 18, 26, 31, 33, 34, 38, 42, 44, 45, 46, 47, 51, 53 }),
        ["bolt"] = (8, 8, new[] {
            11, 12, 13, 20, 27, 34, 35, 41, 43, 50 }),
        ["gear"] = (8, 8, new[] {
            26, 27, 28, 29, 33, 35, 36, 37, 38, 42, 44, 45, 46, 47, 51, 53 }),
        ["wifi"] = (8, 8, new[] {
            25, 26, 29, 30, 33, 34, 35, 36, 37, 38, 40, 41, 42, 43, 44, 45, 46, 47, 48, 51, 52, 53 }),
        ["clock"] = (8, 8, new[] {
            10, 11, 12, 13, 14, 17, 23, 24, 31, 32, 39, 40, 47, 49, 50, 51, 52, 53 }),
        ["car"] = (8, 8, new[] {
            8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 19, 20, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 34, 36, 39, 40, 41, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52 }),
        ["person"] = (8, 8, new[] {
            19, 20, 21, 27, 29, 35, 36, 37, 43, 45, 50, 51, 52, 53, 54 }),
        ["wrench"] = (8, 8, new[] {
            11, 12, 20, 21, 28, 29, 35, 36, 37, 43, 45, 51, 52, 53 }),
        ["computer"] = (8, 8, new[] {
            1, 2, 3, 4, 5, 6, 8, 15, 16, 23, 24, 31, 32, 39, 40, 47, 48, 55, 49, 50, 51, 52, 53, 54, 56, 57, 58, 59, 60, 61, 62, 63 }),
        ["food"] = (8, 8, new[] {
            9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 23, 26, 31, 34, 40, 42, 49, 50, 51 }),
        ["music"] = (8, 8, new[] {
            11, 19, 26, 27, 33, 34, 35, 41, 43, 49, 51 }),
        ["dollar"] = (8, 8, new[] {
            11, 18, 19, 20, 21, 22, 27, 34, 35, 36, 37, 43, 49, 50, 51, 52, 53 }),
    };

    public static (int w, int h, int[] bits) Get(string id) =>
        Defs.TryGetValue(id, out var v) ? v : (8, 8, Array.Empty<int>());
}
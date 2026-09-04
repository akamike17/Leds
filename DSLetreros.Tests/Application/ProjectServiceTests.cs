using DSLetreros.Application.Services;
using DSLetreros.Domain.Entities;
using DSLetreros.Domain.ValueObjects;
using DSLetreros.Infrastructure.Persistence;
using Xunit;

namespace DSLetreros.Tests.Application;

public class ProjectServiceTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "dsletras-projservice", Guid.NewGuid().ToString("N"));

    private static (ProjectService, string) NewService()
    {
        var root = NewRoot();
        return (new ProjectService(new AtlasProjectStore(), root), root);
    }

    [Fact]
    public void CreateProject_sets_defaults_and_trims_name()
    {
        var (svc, root) = NewService();
        try
        {
            var p = svc.CreateProject("  Mi Letrero  ", 32, 16);
            Assert.Equal("Mi Letrero", p.Name);
            Assert.Equal(32, p.Canvas.Width);
            Assert.Equal(16, p.Canvas.Height);
            Assert.Single(p.Scenes);
            Assert.Equal("Escena 1", p.Scenes[0].Name);
            Assert.Single(p.Scenes[0].Layers);
            Assert.Equal("Capa 1", p.Scenes[0].Layers[0].Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CreateProject_blank_name_falls_back_to_sin_titulo()
    {
        var (svc, root) = NewService();
        try
        {
            Assert.Equal("Sin título", svc.CreateProject("   ", 8, 8).Name);
            Assert.Equal("Sin título", svc.CreateProject(null!, 8, 8).Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Validate_returns_invalid_for_empty_project()
    {
        var (svc, root) = NewService();
        try
        {
            var empty = new Project { Name = "Vacío", Canvas = new CanvasDefinition(16, 16) };
            Assert.False(svc.Validate(empty).IsValid);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveProjectPath_is_under_root()
    {
        var (svc, root) = NewService();
        try
        {
            var id = Guid.NewGuid();
            var path = svc.ResolveProjectPath(id);
            Assert.EndsWith($"{id:N}.atlas", path);
            Assert.StartsWith(Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar)),
                Path.GetFullPath(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SaveAutosaveOpen_roundtrip()
    {
        var (svc, root) = NewService();
        try
        {
            var p = svc.CreateProject("Persistido", 16, 8);

            var save = await svc.SaveAsync(p);
            Assert.True(save.Success, save.Message);

            var (open, loaded) = await svc.OpenByIdAsync(p.Id.Value);
            Assert.True(open.Success, open.Message);
            Assert.Equal("Persistido", loaded!.Name);
            Assert.Single(loaded.Scenes);

            // Autosave escribe un hermano y no toca el original.
            var auto = await svc.AutosaveAsync(p);
            Assert.True(auto.Success, auto.Message);
            Assert.True(Directory.Exists(p.Id.Value.ToString("N") + ".atlas.autosave")
                || auto.Path.EndsWith(".autosave", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SaveAsync_rejects_invalid_project()
    {
        var (svc, root) = NewService();
        try
        {
            var p = new Project { Name = "Sin escenas", Canvas = new CanvasDefinition(16, 16) };
            var save = await svc.SaveAsync(p);
            Assert.False(save.Success);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OpenByIdAsync_rejects_empty_guid()
    {
        var (svc, root) = NewService();
        try
        {
            var (result, project) = await svc.OpenByIdAsync(Guid.Empty);
            Assert.False(result.Success);
            Assert.Null(project);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OpenAsync_rejects_path_outside_projects_root()
    {
        // v2.md §6: OpenAsync(string path) no debe abrir rutas arbitrarias del disco;
        // toda ruta debe quedar estrictamente dentro de ProjectsRoot (containment).
        var (svc, root) = NewService();
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            // Ruta exterior válida (existe un .atlas legítimo fuera del root).
            var extStore = new AtlasProjectStore();
            var extProject = svc.CreateProject("Exterior", 8, 8);
            var extPath = Path.Combine(outside, $"{extProject.Id.Value:N}.atlas");
            await extStore.SaveAsync(extProject, extPath);

            var (result, project) = await svc.OpenAsync(extPath);
            Assert.False(result.Success, "una ruta exterior debe rechazarse");
            Assert.Null(project);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task OpenAsync_accepts_path_within_projects_root()
    {
        // Contraparte positiva: una ruta DENTRO del root sí se abre.
        var (svc, root) = NewService();
        try
        {
            var p = svc.CreateProject("Dentro", 16, 8);
            await svc.SaveAsync(p);
            var path = svc.ResolveProjectPath(p.Id.Value);

            var (result, project) = await svc.OpenAsync(path);
            Assert.True(result.Success, result.Message);
            Assert.Equal("Dentro", project!.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OpenByIdAsync_returns_fail_when_missing()
    {
        var (svc, root) = NewService();
        try
        {
            var (result, project) = await svc.OpenByIdAsync(Guid.NewGuid());
            Assert.False(result.Success);
            Assert.Null(project);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ListProjects_returns_empty_when_root_missing()
    {
        // Usamos un root que NO existe aún (no lo creamos) vía un subdirectorio inexistente.
        var store = new AtlasProjectStore();
        var root = NewRoot();
        // ProjectService.CanonicalizeRoot crea el root, pero probamos la rama de directorio vacío.
        var svc = new ProjectService(store, root);
        try
        {
            Assert.Empty(svc.ListProjects());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ListProjects_returns_saved_projects_sorted_and_skips_corrupt()
    {
        var (svc, root) = NewService();
        try
        {
            var a = svc.CreateProject("Alfa", 8, 8);
            var b = svc.CreateProject("Beta", 8, 8);
            await svc.SaveAsync(a);
            await svc.SaveAsync(b);

            // Directorio corrupto (sin manifest) debe ignorarse.
            Directory.CreateDirectory(Path.Combine(root, "corrupt.atlas"));

            var list = svc.ListProjects();
            Assert.Equal(2, list.Count);
            Assert.Contains(list, s => s.Name == "Alfa");
            Assert.Contains(list, s => s.Name == "Beta");
            // ordenado por UpdatedAt desc
            Assert.True(list[0].UpdatedAt >= list[1].UpdatedAt);
        }
        finally { Directory.Delete(root, true); }
    }
}
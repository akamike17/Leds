using Microsoft.AspNetCore.Mvc;

namespace DSLetreros.Controllers;

public class PlaybackController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}

public class SettingsController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
using DSLetreros.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace DSLetreros.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
                {
                    // Portada mínima de DSLetras (spec P1 UX): accesos directos, no redirect.
                    return View();
                }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
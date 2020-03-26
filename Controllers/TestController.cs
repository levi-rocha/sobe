using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using SOBE.Models;

namespace SOBE.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        private IWebHostEnvironment _env;
        private Random _random;
        private string _path => "result.txt";

        public TestController(IWebHostEnvironment env)
        {
            _env = env;
            _random = new Random(DateTime.Now.Second);
        }

        [HttpGet]
        public ActionResult Get()
        {
            if (System.IO.File.Exists(_path))
                return Content(System.IO.File.ReadAllText(_path));
            return NotFound();
        }

        [HttpPost]
        public ActionResult Post(Input input)
        {   
            Task.Run(() => {
                Thread.Sleep(_random.Next(1, 9)*1000);
                System.IO.File.WriteAllTextAsync(_path, input?.Value ?? "null");
            });
            return Content(input?.Value ?? "null");
        }

        public class Input { public string Value { get; set; } }
    }
}
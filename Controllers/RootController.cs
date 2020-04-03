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
    [Route("")]
    public class RootController : ControllerBase
    {
        [HttpGet]
        public ActionResult Get()
        {
            return Content(@"Usage: 
1. POST /download {'FileUrl': 'http://example.com/example.exe', 'OutputName': 'myfile.exe'}
    result: {
        'readyForDownload' : false,
        'finished' : false,
        'id' : REQUESTID,
        'message' : 'File successfully submitted for processing'
    }
2. GET /download/status?requestId=REQUESTID
    result: {
        'readyForDownload' : true,
        'finished' : true,
        'id' : REQUESTID,
    }
3. GET /download?requestId=REQUESTID
    result: Content-Type: application/octet-stream | Content-Disposition: attachment
");
        }
    }
}
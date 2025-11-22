using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        private readonly IConfiguration _config;

        public HealthController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("mongo")]
        public IActionResult CheckMongo()
        {
            try
            {
                var conn = _config["Mongo:ConnectionString"];
                var dbName = _config["Mongo:DatabaseName"];

                var client = new MongoClient(conn);
                var db = client.GetDatabase(dbName);

                // Try to list collections (forces connection check)
                var collections = db.ListCollectionNames().ToList();

                return Ok(new
                {
                    status = "ok",
                    database = dbName,
                    collections
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = "fail",
                    error = ex.Message
                });
            }
        }
    }
}

using MailVoidCommon;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Dapper;

namespace MailVoidApi.Controllers;
[Authorize]
[ApiController]
[Route("api/mail")]
public class MailController : ControllerBase
{
    private readonly ILogger<MailController> _logger;
    private readonly IDbConnectionFactory _dbFactory;
    public MailController(ILogger<MailController> logger, IDbConnectionFactory dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMail(long id)
    {
        using (var db = _dbFactory.OpenDbConnection())
        {
            var email = await db.SingleByIdAsync<Mail>(id);
            if (email == null)
            {
                return NotFound();
            }
            return Ok(email);
        }
    }
    [HttpGet("boxes")]
    public async Task<IEnumerable<string>> GetBoxes()
    {
        using (var db = _dbFactory.OpenDbConnection())
        {
            var boxes = await db.SelectAsync<string>("SELECT DISTINCT Mail.To FROM Mail");
            return boxes;
        }

    }
    [HttpPost]
    public async Task<IActionResult> GetMails([FromBody] FilterOptions? options)
    {


        using (var db = _dbFactory.OpenDbConnection())
        {

            var query = "select * FROM Mail";
            if (!string.IsNullOrEmpty(options?.To))
            {
                query += " WHERE Mail.To = @To";
            }

            return Ok(await db.QueryAsync<Mail>(query, new { To = options?.To }));
        }
    }
    [HttpDelete("boxes")]
    public async Task<IActionResult> DeleteBox([FromBody] FilterOptions options)
    {
        if (options == null || string.IsNullOrEmpty(options.To))
            return BadRequest();

        using (var db = _dbFactory.OpenDbConnection())
        {

            var query = "DELETE FROM Mail WHERE Mail.To = @To";
            await db.ExecuteAsync(query, new { options.To });
            return Ok();
        }
    }
}

public class FilterOptions
{
    public string? To { get; set; }
}
public class MailViewModel
{
    public required List<string> Mailboxes { get; set; }
    public required List<Mail> Emails { get; set; }
}

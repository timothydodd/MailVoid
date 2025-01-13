using MailVoidApi.Services;
using MailVoidWeb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    private readonly MailGroupService _mailGroupService;
    private readonly IUserService _userService;
    public MailController(ILogger<MailController> logger, IDbConnectionFactory dbFactory, MailGroupService mailGroupService, IUserService userService)
    {
        _logger = logger;
        _dbFactory = dbFactory;
        _mailGroupService = mailGroupService;
        _userService = userService;
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
    public async Task<IActionResult> GetMails([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FilterOptions? options = null)
    {

        options ??= new FilterOptions();
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
    [HttpGet("groups")]
    public async Task<IActionResult> GetMailGroups()
    {
        using (var db = _dbFactory.OpenDbConnection())
        {
            var groups = await db.SelectAsync<MailGroup>();
            return Ok(groups);
        }
    }
    [HttpPost("groups")]
    public async Task<IActionResult> SaveMailGroup([FromBody] MailGroupRequest groupRequest)
    {

        var group = groupRequest.ToMailGroup(_userService.GetUserId());
        if (group.Id == 0)
        {
            using (var db = _dbFactory.OpenDbConnection())
            {

                group.Id = await db.InsertAsync(group);
            }
        }
        else
        {
            using (var db = _dbFactory.OpenDbConnection())
            {
                var query = "UPDATE MailGroup SET Path = @Path, Rules = @Rules WHERE Id = @Id";
                await db.ExecuteAsync(query, group);
            }
        }
        await _mailGroupService.UpdateMailsByMailGroupPattern(group);
        return Ok();
    }

}
public record MailGroupRequest
{
    public long? Id { get; set; }
    public required string Path { get; set; }
    public required string Rules { get; set; }

    public MailGroup ToMailGroup(Guid userId)
    {
        return new MailGroup()
        {
            Id = Id ?? 0,
            Path = Path,
            Rules = Rules,
            OwnerUserId = userId
        };
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

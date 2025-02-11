using MailVoidApi.Common;
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
    private readonly IMailGroupService _mailGroupService;
    private readonly IUserService _userService;

    public MailController(ILogger<MailController> logger, IDbConnectionFactory dbFactory, IMailGroupService mailGroupService, IUserService userService)
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
    public async Task<IEnumerable<MailBox>> GetBoxes()
    {
        using (var db = _dbFactory.OpenDbConnection())
        {
            var boxes = await db.SelectAsync<Mail>("SELECT DISTINCT Mail.To,Mail.MailGroupPath FROM Mail");
            return boxes.Select(x =>
            {
                return new MailBox()
                {
                    Name = x.To,
                    Path = x.MailGroupPath
                };
            });
        }

    }
    [HttpPost]
    public async Task<PagedResults<Mail>> GetMails([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FilterOptions? options = null)
    {
        var results = new PagedResults<Mail>();


        options ??= new FilterOptions();
        using (var db = _dbFactory.OpenDbConnection())
        {


            var p = new
            {
                To = options.To,
                Offset = (options.Page - 1) * options.PageSize,
                PageSize = options.PageSize
            };
            var query = "select * FROM Mail";
            if (!string.IsNullOrEmpty(options.To))
            {
                query += " WHERE Mail.To = @To";
            }
            if (options.PageSize == 1)
            {
                var countQuery = $"Select Count(b.*) From ({query}) as b";
                results.TotalCount = await db.QuerySingleAsync<long>(query, p);
            }
            query += " ORDER BY CreatedOn DESC LIMIT @PageSize OFFSET @Offset";

            results.Items = await db.QueryAsync<Mail>(query, p);
            return results;
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
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? To { get; set; }
}
public class MailBox
{
    public string? Path { get; set; }
    public required string Name { get; set; }

}

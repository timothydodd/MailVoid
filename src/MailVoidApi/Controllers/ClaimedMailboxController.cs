using MailVoidApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MailVoidApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ClaimedMailboxController : ControllerBase
    {
        private readonly IClaimedMailboxService _claimedMailboxService;
        private readonly AuthService _authService;
        private readonly ILogger<ClaimedMailboxController> _logger;

        public ClaimedMailboxController(IClaimedMailboxService claimedMailboxService, AuthService authService, ILogger<ClaimedMailboxController> logger)
        {
            _claimedMailboxService = claimedMailboxService;
            _authService = authService;
            _logger = logger;
        }

        [HttpGet("my-mailboxes")]
        public async Task<ActionResult<List<ClaimedMailboxDto>>> GetMyClaimedMailboxes()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var claimedMailboxes = await _claimedMailboxService.GetUserClaimedMailboxesAsync(userId.Value);
            var dtos = claimedMailboxes.Select(cm => new ClaimedMailboxDto
            {
                Id = cm.Id,
                EmailAddress = cm.EmailAddress,
                ClaimedOn = cm.ClaimedOn,
                IsActive = cm.IsActive
            }).ToList();

            return Ok(dtos);
        }

        [HttpGet("unclaimed")]
        public async Task<ActionResult<List<string>>> GetUnclaimedEmailAddresses()
        {
            var unclaimedEmails = await _claimedMailboxService.GetUnclaimedEmailAddressesAsync();
            return Ok(unclaimedEmails);
        }

        [HttpPost("claim")]
        public async Task<ActionResult<ClaimedMailboxDto>> ClaimMailbox([FromBody] ClaimMailboxRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.EmailAddress))
            {
                return BadRequest("Email address is required");
            }

            // Check if email is already claimed
            if (await _claimedMailboxService.IsEmailClaimedAsync(request.EmailAddress))
            {
                return Conflict("This email address is already claimed by another user");
            }

            var claimedMailbox = await _claimedMailboxService.ClaimMailboxAsync(userId.Value, request.EmailAddress);
            if (claimedMailbox == null)
            {
                return BadRequest("Failed to claim mailbox");
            }

            var dto = new ClaimedMailboxDto
            {
                Id = claimedMailbox.Id,
                EmailAddress = claimedMailbox.EmailAddress,
                ClaimedOn = claimedMailbox.ClaimedOn,
                IsActive = claimedMailbox.IsActive
            };

            return CreatedAtAction(nameof(GetMyClaimedMailboxes), new { id = dto.Id }, dto);
        }

        [HttpDelete("unclaim")]
        public async Task<ActionResult> UnclaimMailbox([FromBody] UnclaimMailboxRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.EmailAddress))
            {
                return BadRequest("Email address is required");
            }

            var success = await _claimedMailboxService.UnclaimMailboxAsync(userId.Value, request.EmailAddress);
            if (!success)
            {
                return NotFound("Mailbox not found or not owned by current user");
            }

            return NoContent();
        }

        [HttpGet("check/{emailAddress}")]
        public async Task<ActionResult<bool>> IsEmailClaimed(string emailAddress)
        {
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return BadRequest("Email address is required");
            }

            var isClaimed = await _claimedMailboxService.IsEmailClaimedAsync(emailAddress);
            return Ok(isClaimed);
        }

        private Guid? GetCurrentUserId()
        {
            return _authService.GetUserIdFromPrincipal(User);
        }
    }

    public class ClaimedMailboxDto
    {
        public long Id { get; set; }
        public required string EmailAddress { get; set; }
        public DateTime ClaimedOn { get; set; }
        public bool IsActive { get; set; }
    }

    public class ClaimMailboxRequest
    {
        public required string EmailAddress { get; set; }
    }

    public class UnclaimMailboxRequest
    {
        public required string EmailAddress { get; set; }
    }
}

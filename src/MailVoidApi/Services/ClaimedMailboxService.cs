using MailVoidApi.Data;
using MailVoidWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MailVoidApi.Services
{
    public interface IClaimedMailboxService
    {
        Task<List<ClaimedMailbox>> GetUserClaimedMailboxesAsync(Guid userId);
        Task<List<string>> GetUnclaimedEmailAddressesAsync();
        Task<ClaimedMailbox?> ClaimMailboxAsync(Guid userId, string emailAddress);
        Task<bool> UnclaimMailboxAsync(Guid userId, string emailAddress);
        Task<bool> IsEmailClaimedAsync(string emailAddress);
        Task<ClaimedMailbox?> GetClaimedMailboxAsync(string emailAddress);
    }

    public class ClaimedMailboxService : IClaimedMailboxService
    {
        private readonly MailVoidDbContext _context;
        private readonly ILogger<ClaimedMailboxService> _logger;

        public ClaimedMailboxService(MailVoidDbContext context, ILogger<ClaimedMailboxService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<ClaimedMailbox>> GetUserClaimedMailboxesAsync(Guid userId)
        {
            return await _context.ClaimedMailboxes
                .Include(cm => cm.User)
                .Where(cm => cm.UserId == userId && cm.IsActive)
                .OrderBy(cm => cm.EmailAddress)
                .ToListAsync();
        }

        public async Task<List<string>> GetUnclaimedEmailAddressesAsync()
        {
            // Get distinct email addresses from mails that don't have a user- path
            var allEmailAddresses = await _context.Mails
                .Where(m => !string.IsNullOrEmpty(m.To) && 
                           (string.IsNullOrEmpty(m.MailGroupPath) || !m.MailGroupPath.StartsWith("user-")))
                .Select(m => m.To)
                .Distinct()
                .ToListAsync();

            // Filter out already claimed ones
            var claimedEmailAddresses = await _context.ClaimedMailboxes
                .Where(cm => cm.IsActive)
                .Select(cm => cm.EmailAddress)
                .ToListAsync();

            return allEmailAddresses.Except(claimedEmailAddresses).OrderBy(e => e).ToList();
        }

        public async Task<ClaimedMailbox?> ClaimMailboxAsync(Guid userId, string emailAddress)
        {
            try
            {
                // Check if already claimed
                var existingClaim = await _context.ClaimedMailboxes
                    .FirstOrDefaultAsync(cm => cm.EmailAddress == emailAddress && cm.IsActive);

                if (existingClaim != null)
                {
                    _logger.LogWarning("Email address {EmailAddress} is already claimed by user {UserId}", 
                        emailAddress, existingClaim.UserId);
                    return null;
                }

                // Get user to get username for path
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return null;
                }

                var claimedMailbox = new ClaimedMailbox
                {
                    EmailAddress = emailAddress,
                    UserId = userId,
                    ClaimedOn = DateTime.UtcNow,
                    IsActive = true
                };

                _context.ClaimedMailboxes.Add(claimedMailbox);

                // Update existing mails to use the user path
                var userPath = claimedMailbox.GetMailGroupPath(user.UserName);
                var existingMails = await _context.Mails
                    .Where(m => m.To == emailAddress && 
                               (string.IsNullOrEmpty(m.MailGroupPath) || !m.MailGroupPath.StartsWith("user-")))
                    .ToListAsync();

                foreach (var mail in existingMails)
                {
                    mail.MailGroupPath = userPath;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully claimed mailbox {EmailAddress} for user {UserId}. Updated {MailCount} existing mails.", 
                    emailAddress, userId, existingMails.Count);

                return claimedMailbox;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error claiming mailbox {EmailAddress} for user {UserId}", emailAddress, userId);
                return null;
            }
        }

        public async Task<bool> UnclaimMailboxAsync(Guid userId, string emailAddress)
        {
            try
            {
                var claimedMailbox = await _context.ClaimedMailboxes
                    .FirstOrDefaultAsync(cm => cm.EmailAddress == emailAddress && cm.UserId == userId && cm.IsActive);

                if (claimedMailbox == null)
                {
                    return false;
                }

                // Deactivate the claim
                claimedMailbox.IsActive = false;

                // Reset mail paths back to null or empty
                var userMails = await _context.Mails
                    .Where(m => m.To == emailAddress && 
                               !string.IsNullOrEmpty(m.MailGroupPath) && 
                               m.MailGroupPath.StartsWith("user-"))
                    .ToListAsync();

                foreach (var mail in userMails)
                {
                    mail.MailGroupPath = null; // Reset to unclaimed state
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully unclaimed mailbox {EmailAddress} for user {UserId}. Reset {MailCount} mails.", 
                    emailAddress, userId, userMails.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unclaiming mailbox {EmailAddress} for user {UserId}", emailAddress, userId);
                return false;
            }
        }

        public async Task<bool> IsEmailClaimedAsync(string emailAddress)
        {
            return await _context.ClaimedMailboxes
                .AnyAsync(cm => cm.EmailAddress == emailAddress && cm.IsActive);
        }

        public async Task<ClaimedMailbox?> GetClaimedMailboxAsync(string emailAddress)
        {
            return await _context.ClaimedMailboxes
                .Include(cm => cm.User)
                .FirstOrDefaultAsync(cm => cm.EmailAddress == emailAddress && cm.IsActive);
        }
    }
}
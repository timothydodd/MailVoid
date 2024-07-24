using MailVoidCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MailVoidWeb.Pages.Email
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly MailDbContext _context;
        [BindProperty(SupportsGet = true)]
        public string SelectedMailbox { get; set; }
        public IndexModel(ILogger<IndexModel> logger, MailDbContext context)
        {
            _logger = logger;
            _context = context;
        }
        public MailViewModel MailViewModel { get; set; }
        public async Task OnGetAsync()
        {
            // Get unique From addresses
            var mailboxes = await _context.Mail
                                .Select(m => m.To)
                                .Distinct()
                                .ToListAsync();

            // Get emails based on the selected mailbox
            var emails = string.IsNullOrEmpty(SelectedMailbox)
                ? await _context.Mail.ToListAsync()
                : await _context.Mail.Where(m => m.To == SelectedMailbox).ToListAsync();

            MailViewModel = new MailViewModel
            {
                Mailboxes = mailboxes,
                Emails = emails
            };
        }

        public async Task<IActionResult> OnGetEmailsByMailboxAsync(string mailbox)
        {
            var emails = await _context.Mail
                .Where(m => m.To == mailbox)
                .ToListAsync();

            return new JsonResult(emails);
        }
    }
}
public class MailViewModel
{
    public required List<string> Mailboxes { get; set; }
    public required List<Mail> Emails { get; set; }
}

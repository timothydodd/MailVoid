using MailVoidCommon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MailVoidWeb.Pages.Email
{
    public class DetailsModel : PageModel
    {
        private readonly MailDbContext _context;

        public DetailsModel(MailDbContext context)
        {
            _context = context;
        }

        public Mail Email { get; set; }

        public async Task<IActionResult> OnGetAsync(long id)
        {
            Email = await _context.Mail.FindAsync(id);

            if (Email == null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}

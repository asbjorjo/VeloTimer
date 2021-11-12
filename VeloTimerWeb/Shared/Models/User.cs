using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class User : IdentityUser<Guid>
    {
    }
}

#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sanakan.Extensions;
using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;
using Sanakan.Config;

namespace Sanakan.Api.Controllers
{
    [ApiController, Authorize(Policy = "Site")]
    [Route("api/[controller]")]
    public class QuizController : ControllerBase
    {
        private IConfig _config;

        public QuizController(IConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Pobiera listę pytań
        /// </summary>
        [HttpGet("questions")]
        public async Task<ActionResult<List<Database.Models.Question>>> GetQuestionsAsync()
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                return await db.GetCachedAllQuestionsAsync();
            }
        }

        /// <summary>
        /// Pobiera pytanie po id
        /// </summary>
        /// <param name="id">id pytania</param>
        /// <response code="500">Internal Server Error</response>
        [HttpGet("question/{id}")]
        public async Task<ActionResult<Database.Models.Question>> GetQuestionAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                return await db.GetCachedQuestionAsync(id);
            }
        }

        /// <summary>
        /// Dodaje nowe pytanie
        /// </summary>
        /// <param name="question">pytanie</param>
        /// <response code="500">Internal Server Error</response>
        [HttpPost("question")]
        public async Task<IActionResult> AddQuestionAsync([FromBody]Database.Models.Question question)
        {
            var invalid = question == null
                || string.IsNullOrWhiteSpace(question.Content)
                || question.Answers == null
                || question.Answers.Count < 2
                || question.Answers.Any(a => a == null || a.Number < 1 || a.Number > 9)
                || question.Answers.Select(a => a.Number).Distinct().Count() != question.Answers.Count
                || !question.Answers.Any(a => a.Number == question.Answer);

            if (invalid)
                return "Question is invalid!".ToResponse(400);

            using (var db = new Database.DatabaseContext(_config))
            {
                db.Questions.Add(question);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.Quiz });
            }
            return "Question added!".ToResponse(200);
        }

        /// <summary>
        /// Usuwa pytanie
        /// </summary>
        /// <param name="id">id pytania</param>
        /// <response code="404">Question not found</response>
        /// <response code="500">Internal Server Error</response>
        [HttpDelete("question/{id}")]
        public async Task<IActionResult> RemoveQuestionAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var question = await db.Questions.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == id);
                if (question != null)
                {
                    db.Questions.Remove(question);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { CacheTags.Quiz });

                    return "Question removed!".ToResponse(200);
                }
            }
            return "Question not found!".ToResponse(404);
        }
    }
}
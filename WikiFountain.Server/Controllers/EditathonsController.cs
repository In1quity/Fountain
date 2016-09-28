﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using NHibernate.Linq;
using WikiFountain.Server.Core;
using WikiFountain.Server.Models;
using WikiFountain.Server.Models.Rules;

namespace WikiFountain.Server.Controllers
{
    public class EditathonsController : ApiControllerWithDb
    {
        private readonly Identity _identity;

        public EditathonsController(Identity identity)
        {
            _identity = identity;
        }

        public HttpResponseMessage Get()
        {
            return Ok(Session.Query<Editathon>().OrderByDescending(e => e.Finish).Select(e => new
            {
                e.Code,
                e.Name,
                e.Description,
                e.Start,
                e.Finish,
            }).ToList());
        }

        public HttpResponseMessage Get(string code)
        {
            var e = Session.Query<Editathon>()
                .FetchMany(_ => _.Articles).ThenFetch(a => a.Marks)
                .Fetch(_ => _.Jury)
                .Fetch(_ => _.Rules)
                .SingleOrDefault(i => i.Code == code);

            if (e == null)
                return NotFound();
            return Ok(new
            {
                e.Code,
                e.Name,
                e.Description,
                e.Start,
                e.Finish,
                e.Jury,
                Rules = e.Rules.Select(r => new
                {
                    r.Type,
                    r.Severity,
                    r.Params,
                }),
                Articles = e.Articles.OrderByDescending(a => a.DateAdded).Select(a => new
                {
                    a.Id,
                    a.DateAdded,
                    a.Name,
                    a.User,
                    Marks = a.Marks.Select(m => new
                    {
                        m.User,
                        m.Marks,
                        m.Comment,
                    }),
                }),
            });
        }

        public class ArticlePostData
        {
            public string Title { get; set; }
        }

        [HttpPost]
        public async Task<HttpResponseMessage> AddArticle(string code, [FromBody] ArticlePostData body)
        {
            var user = _identity.GetUserInfo();
            if (user == null)
                return Unauthorized();

            var e = Session.Query<Editathon>()
                .Fetch(_ => _.Rules)
                .Fetch(_ => _.Articles)
                .SingleOrDefault(i => i.Code == code);

            if (e == null)
                return NotFound();

            var now = DateTime.UtcNow;
            if (now < e.Start || now.Date > e.Finish)
                return Forbidden();

            if (e.Articles.Any(a => a.Name == body.Title))
                return Forbidden();

            var wiki = new MediaWiki("https://ru.wikipedia.org/w/api.php", _identity);

            var page = await wiki.GetPage(body.Title);
            if (page == null)
                return Forbidden();

            var rules = e.Rules
                .Where(r => r.Severity == RuleSeverity.Requirement)
                .Select(r => r.Get())
                .ToArray();

            if (rules.Any())
            {
                var loader = new ArticleDataLoader(rules.SelectMany(r => r.GetReqs()));
                var data = await loader.LoadAsync(wiki, body.Title);

                var ctx = new RuleContext { User = user };
                foreach (var rule in rules)
                {
                    if (!rule.Check(data, ctx))
                        return Forbidden();
                }
            }

            var template = new Template
            {
                Name = "Марафон юниоров",
                Args =
                { 
                    new Template.Argument { Value = user.Username },
                    new Template.Argument { Name = "статус", Value = "Готово" },
                }
            } + "\n";

            var templateIndex = FindTemplatePos(page);
            if (templateIndex == null)
            {
                page = page.Insert(0, template);
            }
            else
            {
                var existingTemplate = Template.ParseAt(page, templateIndex.Value);
                page = page.Remove(templateIndex.Value, existingTemplate.ToString().Length).Insert(templateIndex.Value, template);
            }
            await wiki.EditPage(body.Title, page, "Автоматическая простановка шаблона");

            e.Articles.Add(new Article
            {
                Name = body.Title,
                User = user.Username,
                DateAdded = now,
            });

            return Ok();
        }

        private static int? FindTemplatePos(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\{\{(?:[\s_]*[Мм]арафон[ _]+юниоров[\s_]*)[|}\s]");
            if (!match.Success)
                return null;
            return match.Index;
        }

        public class MarkPostData
        {
            public string Title { get; set; }
            public string Marks { get; set; }
            public string Comment { get; set; }
        }

        [HttpPost]
        public HttpResponseMessage SetMark(string code, [FromBody] MarkPostData body)
        {
            var user = _identity.GetUserInfo();
            if (user == null)
                return Unauthorized();

            var e = Session.Query<Editathon>()
                .FetchMany(_ => _.Articles).ThenFetch(a => a.Marks)
                .Fetch(_ => _.Jury)
                .SingleOrDefault(i => i.Code == code);

            if (e == null)
                return NotFound();

            if (!e.Jury.Contains(user.Username))
                return Forbidden();

            var article = e.Articles.SingleOrDefault(a => a.Name == body.Title);
            if (article == null)
                return NotFound();

            var mark = article.Marks.SingleOrDefault(m => m.User == user.Username);

            if (mark == null)
            {
                mark = new Mark
                {
                    Article = article,
                    User = user.Username,
                };
                article.Marks.Add(mark);
            }

            mark.Marks = JObject.Parse(body.Marks);
            mark.Comment = body.Comment;

            return Ok();
        }
    }
}

(function captureTibo(document, pageUrl) {
  const location = new URL(pageUrl);
  const path = location.pathname;
  const isTibo = /^\/thsottiaux(?:\/(?:with_replies|status\/\d+))?\/?$/i.test(path);
  if (location.hostname !== "x.com" || !isTibo) {
    return { Posts: [], PageUrl: location.href, IsTiboPage: false,
      LoginRequired: /\/i\/flow\/login/.test(path) };
  }
  const posts = [];
  function quoted(node, article) {
    for (let parent = node.parentElement; parent && parent !== article; parent = parent.parentElement) {
      if (parent.getAttribute("role") === "link" && parent.tagName !== "A") return true;
    }
    return false;
  }
  for (const article of document.querySelectorAll("article")) {
    const author = article.querySelector('[data-testid="User-Name"]');
    if (!author || !Array.from(author.querySelectorAll("a[href]")).some(a =>
      /^\/thsottiaux\/?$/i.test(a.getAttribute("href")) && a.textContent.trim() === "@thsottiaux")) continue;
    const body = article.querySelector('[data-testid="tweetText"]');
    if (!body || quoted(body, article)) continue;
    const time = Array.from(article.querySelectorAll("time[datetime]")).find(t => {
      const link = t.closest("a[href]");
      if (!link || !/^\/thsottiaux\/status\/\d+$/i.test(link.getAttribute("href"))) return false;
      return !quoted(t, article);
    });
    if (!time) continue;
    const href = time.closest("a").getAttribute("href");
    const id = href.match(/\/status\/(\d+)$/)[1];
    const text = body.innerText.trim();
    if (!text || text.length > 12000 || !Number.isFinite(Date.parse(time.dateTime))) continue;
    if (posts.some(p => p.Id === id)) continue;
    posts.push({ Id: id, Text: text, PublishedAt: time.dateTime,
      Url: "https://x.com/thsottiaux/status/" + id, ContextIds: [] });
    if (posts.length >= 100) break;
  }
  // Same-page context supports a heuristic association, not a verified reply relation.
  for (const post of posts) post.ContextIds = posts.filter(p => p.Id !== post.Id).map(p => p.Id);
  return { Posts: posts, PageUrl: "https://x.com" + path, IsTiboPage: true,
    LoginRequired: posts.length === 0 && !!document.querySelector('a[href="/login"],a[href="/i/flow/login"]') };
})

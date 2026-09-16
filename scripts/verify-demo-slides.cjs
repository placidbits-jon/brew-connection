#!/usr/bin/env node
// Run through the deck in order: reset once, preserve the earlier slides' writes.
const {
  chromium,
  expect,
} = require("../src/coffee-community-web/node_modules/@playwright/test");
const { execFileSync } = require("node:child_process");
const { mkdirSync, writeFileSync } = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const root = path.resolve(__dirname, "..");
const web = process.env.DEMO_WEB_URL || "http://localhost:4200";
const api = process.env.DEMO_API_URL || "http://localhost:5130";
const output = path.resolve(
  process.env.DEMO_REPORT_DIR || "/tmp/brew-demo-slides",
);
const checked = expect.configure({ timeout: 15000 });
const report = {
  startedAt: new Date().toISOString(),
  web,
  api,
  slides: [],
  errors: [],
};
let page;
const button = (name) => page.getByRole("button", { name, exact: true });
const visible = async (locator, text) => checked(locator).toContainText(text);
async function runLab(name = "Run query") {
  const response = page.waitForResponse(
    (r) =>
      r.url().endsWith("/api/demo/lab/run") && r.request().method() === "POST",
  );
  await button(name).click();
  const result = await response;
  assert.equal(result.status(), 200);
  await checked(page.locator(".execution")).toBeVisible();
  return result.json();
}
const actions = {
  "A repeatable local stage": async () => {
    await button("Reveal Aspire resource state").click();
    await checked(page.locator(".resource-grid .status-dot.ready")).toHaveCount(
      4,
    );
  },
  "One community, many record types": async () => {
    await button("Person vertex").click();
    await checked(
      page.getByText("Person[slug]", { exact: true }),
    ).toBeVisible();
    await visible(page.locator("main"), "slug");
  },
  "A repeatable community story": async () => {
    await page.getByLabel("Explore as").selectOption("maya-chen");
    const grid = page.locator(".story-grid");
    await visible(grid.locator("article").nth(0), "Priya Nair");
    await visible(grid.locator("article").nth(1), "Blueberry Bloom V60");
    await visible(grid.locator("article").nth(2), "Luis Ortega");
  },
  "Record a meeting": async () => {
    await checked(page.getByLabel("Badge code", { exact: true })).toHaveValue(
      "badge-0003",
    );
    await button("Scan badge").click();
    await visible(
      page.getByRole("region", { name: "Recent meetings" }),
      "Attendee 0003",
    );
    await checked(page.locator(".success")).toBeVisible();
  },
  "Your coffee passport": async () => {
    await button("Love this cup").click();
    await checked(page.locator(".success")).toBeVisible();
    await visible(
      page.locator(".timeline").locator("li").filter({ hasText: "loved" }),
      "Blueberry Bloom V60",
    );
    await button("Inspect Timeline queries").click();
    await visible(
      page.getByRole("region", { name: "Timeline queries" }),
      "Parameters",
    );
  },
  "Who beat me?": async () => {
    await checked(page.locator('#rematch-results')).toHaveCount(0);
    await button("Show rematch candidates").click();
    await button('Inspect Rematch candidates queries').click();
    await visible(page.getByRole('region', {name:'Rematch candidates queries'}), 'BEAT_IN_GAME');
    await checked(page.getByRole('region', {name:'Rematch candidates queries'})).not.toContainText('WANTS_TO_RECONNECT');
    await button('Hide Rematch candidates queries').click();
    await visible(page.locator("#rematches"), "Luis Ortega");
    await visible(page.locator("#rematches"), "Your score 7 · Their score 10");
    await visible(page.locator("#rematches"), "maya-luis-rematch");
  },
  "The shortest social path": async () => {
    await button("Find shortest path").click();
    await checked(
      page
        .getByRole("list", { name: "Shortest connection path" })
        .locator("li"),
    ).toHaveText(["Maya Chen", "Priya Nair", "Luis Ortega"]);
  },
  "People to reconnect with": async () => {
    await checked(page.locator('#reconnect-results')).toHaveCount(0);
    await button("Show reconnect targets").click();
    await button('Inspect Reconnect targets queries').click();
    await visible(page.getByRole('region', {name:'Reconnect targets queries'}), 'WANTS_TO_RECONNECT');
    await button('Hide Reconnect targets queries').click();
    await visible(page.locator("#reconnects"), "Priya Nair");
    await visible(page.locator("#reconnects"), /blueberry/i);
  },
  "A structured recipe": async () => {
    await page
      .getByLabel("Compare prior revision", { exact: true })
      .selectOption("blueberry-v60-v1");
    const current = page
      .locator("details")
      .filter({ hasText: "Current nested recipe document" });
    await visible(current, '"revision": 2');
    await visible(current, '"waterGrams": 300');
    await visible(current, '"temperatureC": 93');
    await checked(page.getByLabel("Grind clicks", { exact: true })).toHaveValue(
      "20",
    );
    await checked(page.locator(".step")).toHaveCount(3);
  },
  "A plain-text memory": async () => {
    await page.getByLabel(/^About/).selectOption("Brew");
    await page
      .getByLabel("Subject slug", { exact: true })
      .fill("blueberry-bloom-v60");
    await button("Load subject notes").click();
    await page
      .getByLabel("Note", { exact: true })
      .fill("Ask Priya about the sweeter finish.");
    await button("Save note").click();
    await visible(
      page.locator('[aria-label="Visible notes"]'),
      "Ask Priya about the sweeter finish.",
    );
    const priyaNotes = page.waitForResponse((r) => {
      const url = new URL(r.url());
      return (
        url.pathname === "/api/demo/notes" &&
        url.searchParams.get("persona") === "priya-nair" &&
        url.searchParams.get("subjectSlug") === "blueberry-bloom-v60"
      );
    });
    await page
      .getByLabel("Demo persona", { exact: true })
      .selectOption("priya-nair");
    assert.equal((await priyaNotes).status(), 200);
    await checked(page.getByLabel(/^About/)).toHaveValue("Brew");
    await checked(page.getByLabel("Subject slug", { exact: true })).toHaveValue(
      "blueberry-bloom-v60",
    );
    await checked(page.getByLabel("Demo persona", { exact: true })).toHaveValue(
      "priya-nair",
    );
    await visible(page.locator(".notes-section"), "as priya-nair");
    await checked(
      page
        .locator(".note-body")
        .filter({ hasText: "Ask Priya about the sweeter finish." }),
    ).toHaveCount(0);
  },
  "Publish without losing history": async () => {
    await page.getByLabel("Grind clicks", { exact: true }).fill("18");
    await button("Publish revision").click();
    await visible(page.locator("#recipe-editor"), "Current revision 3");
    await page
      .getByLabel("Compare prior revision", { exact: true })
      .selectOption("blueberry-v60-v2");
    await visible(page.locator("#revision-history"), "20 → 18");
    await page.getByText("Prior nested document", { exact: true }).click();
    await visible(
      page.locator("details").filter({ hasText: "Prior nested document" }),
      '"revision": 2',
    );
  },
  "Bean-to-cup provenance": async () => {
    await page.getByRole("button", { name: /Pinned revision 2/ }).click();
    await visible(
      page.getByRole("region", { name: "Selected provenance document" }),
      '"revision": 2',
    );
    for (const text of [
      "Ethiopia Guji Lot 17",
      "Great Lakes Roasters",
      "Great Lakes Coffee Table",
      "Priya",
    ])
      await visible(page.locator(".graph"), text);
  },
  "Keyword search": async () => {
    await button("Keyword").click();
    await checked(page.locator(".results h2").first()).toHaveText(
      "Ethiopia Blueberry Bloom",
    );
    await visible(page.locator(".results"), "Blueberry Label Dark Roast");
    await checked(
      page.locator(".results h2").filter({ hasText: "Summer Orchard" }),
    ).toHaveCount(0);
  },
  "Semantic search": async () => {
    await button("Semantic").click();
    await checked(page.locator(".results h2").first()).toHaveText(
      "Blueberry Label Dark Roast",
    );
    await checked(page.locator(".results h2").nth(3)).toHaveText(
      "Summer Orchard",
    );
    await visible(
      page.locator(".results > li").nth(3).locator(".scores > div").first(),
      "0",
    );
  },
  "Hybrid ranking": async () => {
    await button("Hybrid").click();
    await checked(page.locator(".results h2").first()).toHaveText(
      "Ethiopia Blueberry Bloom",
    );
    await visible(page.locator(".results"), "Summer Orchard");
    await visible(page.locator(".results > li").first(), "Vector contribution");
  },
  "Find my next coffee": async () => {
    await button("Personalized").click();
    await checked(page.locator(".results h2").first()).toHaveText(
      "Priya's Honey Stonefruit",
    );
    await visible(
      page.locator(".results > li").first().locator(".evidence"),
      "Priya",
    );
    await visible(
      page.locator(".results > li").first().locator(".evidence"),
      /loved/i,
    );
  },
  "One code, one community record": async () => {
    await button("Look up code").click();
    await visible(page.locator(".identity"), "badge-0001");
    await page
      .locator(".identity")
      .getByRole("link", { name: "Priya Nair", exact: true })
      .click();
    await checked(page).toHaveURL(/\/demo\/passport\/priya-nair$/);
    await visible(page.locator(".subtitle"), "Priya Nair");
  },
  "One more tasting": async () => {
    await checked(page.locator(".counter-value")).toBeVisible();
    const before = Number(await page.locator(".counter-value").innerText());
    await button("Add one tasting").click();
    await checked(page.locator(".counter-value")).toHaveText(
      String(before + 1),
    );
    await visible(page.locator("#live-counter"), "Last increment: +1");
  },
  "Watch the pour": async () => {
    await button("Replay simulation").click();
    await checked(page).toHaveURL(/runId=rehearsal-/);
    await visible(
      page.locator("main"),
      new URL(page.url()).searchParams.get("runId"),
    );
    await checked(page.locator("main")).toContainText(/complete.*60 samples/s, {
      timeout: 60000,
    });
    await checked(
      page.getByLabel("Inspect second", { exact: true }),
    ).toHaveValue("30");
    await visible(page.locator(".metrics"), "120 g");
    await visible(page.locator(".metrics"), "140 g");
    await visible(page.locator(".metrics"), "-20 g");
    await visible(page.locator(".metrics"), "12 / 4 g/s");
    await visible(page.locator(".callout"), "30-second pour spike");
  },
  "The event, in time buckets": async () => {
    await page.getByLabel("Bucket size", { exact: true }).selectOption("10");
    await button("Update buckets").click();
    await checked(page.locator(".bars button")).toHaveCount(12);
    await page.locator(".bars button").first().click();
    await visible(
      page.locator("main"),
      "Selected bucket: 16:00 UTC · 30 events",
    );
    await checked(page.locator(".metrics dd")).toHaveText([
      "360",
      "120",
      "5",
      "3",
    ]);
    await checked(page.locator(".examples table tbody tr")).toHaveCount(4);
    await checked(
      page.locator(".examples table tbody tr td:nth-child(2)"),
    ).toHaveText(["90", "90", "90", "90"]);
  },
  "Nearby coffee, connected": async () => {
    await button("Find nearby").click();
    await page
      .getByRole("button", { name: "1. Great Lakes Coffee Table", exact: true })
      .click();
    await visible(page.locator(".places article").first(), "0.0 m");
    await visible(
      page.locator(".places article").first(),
      "Ethiopia Blueberry Bloom",
    );
    await visible(page.locator(".places article").nth(1), "8.2 m");
    await checked(page.locator(".places article").first()).toHaveClass(
      "selected",
    );
  },
  "One ACID tasting transaction": async () => {
    await button("Run successful tasting").click();
    await checked(page.getByText("Committed", { exact: true })).toBeVisible();
    await visible(
      page.locator("main"),
      "SQL and Cypher returned the same record ID.",
    );
    for (const type of [
      "Brew",
      "Note",
      "BREWED",
      "USED_BATCH",
      "USED_RECIPE",
      "TASTED",
    ]) {
      const row = page
        .locator("tbody tr")
        .filter({
          has: page.getByRole("rowheader", { name: type, exact: true }),
        });
      await visible(row, "+1");
    }
  },
  "What if it fails halfway?": async () => {
    await button("Trigger halfway failure").click();
    await checked(
      page.getByText("All graph and document counts unchanged", {
        exact: true,
      }),
    ).toBeVisible();
    await visible(page.locator("main"), "Rolled back");
    await visible(page.locator("main"), "Inside transaction");
    for (const row of await page.locator("tbody tr").all()) {
      const values = await row.locator("td").allTextContents();
      assert.equal(values[0], values[2], "Visible before/after counts match");
      assert.equal(values[3], "0", "Visible delta is zero");
      const type = await row.locator("th").innerText();
      if (["Brew", "BREWED", "USED_BATCH"].includes(type))
        assert.equal(Number(values[1]), Number(values[0]) + 1);
    }
  },
  "The same record, through another language": async () => {
    const sql = await runLab();
    await visible(page.locator(".result-table"), "Transaction Blueberry V60");
    await page
      .getByRole("button", { name: /Committed Brew through Cypher/ })
      .click();
    await checked(page).toHaveURL(/example=cypher-transaction-brew/);
    const cypher = await runLab();
    assert.equal(sql.records[0].rid, cypher.records[0].rid);
    await visible(page.locator(".result-table"), sql.records[0].rid);
  },
  "Inspect the query plan": async () => {
    await runLab();
    await visible(
      page.locator("main"),
      "FETCH FROM INDEXED FUNCTION SEARCH_INDEX",
    );
    await visible(page.locator(".result-table"), "Ethiopia Blueberry Bloom");
  },
  "Compatibility checkpoint": async () => {
    await runLab("Run compatibility summary");
    await checked(page.locator(".checks article")).toHaveCount(7);
    await checked(page.locator(".result-table tbody tr")).toHaveCount(7);
    await visible(page.locator(".result-table"), "read-completed");
    await visible(page.locator(".limitations"), "does not execute transaction");
  },
};
(async () => {
  mkdirSync(output, { recursive: true });
  const manifest = JSON.parse(
    execFileSync("python3", ["scripts/validate-demo-deck.py", "--json"], {
      cwd: root,
      encoding: "utf8",
    }),
  );
  const slides = Array.isArray(manifest) ? manifest : manifest.features;
  assert.ok(slides.length > 0, "Deck must contain feature slides");
  assert.deepEqual(
    [...slides.map((s) => s.title)].sort(),
    Object.keys(actions).sort(),
    "Every slide needs exactly one browser action, and stale actions must be removed",
  );
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH }
      : {}),
  });
  try {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 1100 },
    });
    page = await context.newPage();
    page.on("pageerror", (error) => report.errors.push(error.message));
    const reset = await context.request.post(`${api}/api/demo/reset`, {
      data: {},
      timeout: 120000,
    });
    assert.equal(reset.status(), 200, await reset.text());
    report.resetCompletedAt = new Date().toISOString();
    for (const slide of slides) {
      const started = Date.now();
      const target = new URL(slide.url);
      const base = new URL(web);
      target.protocol = base.protocol;
      target.host = base.host;
      try {
        await page.goto(target.href);
        await actions[slide.title]();
        for (const inspector of await page.locator('app-query-inspector:visible').all()) {
          const toggle = inspector.getByRole('button');
          await checked(toggle).toBeEnabled();
          if (await toggle.getAttribute('aria-expanded') !== 'true') await toggle.click();
          await checked(inspector.getByRole('region')).toBeVisible();
          await checked(inspector.locator('pre').first()).not.toBeEmpty();
          await toggle.click();
          await checked(inspector.getByRole('region')).toHaveCount(0);
        }
        await checked(page.getByRole("alert")).toHaveCount(0);
        assert.deepEqual(report.errors, [], "No uncaught browser errors");
        if (process.env.DEMO_CAPTURE_SLIDES === "1")
          await page.screenshot({
            path: path.join(output, slide.id + ".png"),
            fullPage: true,
          });
        report.slides.push({
          id: slide.id,
          title: slide.title,
          url: target.href,
          action: slide.action,
          lookFor: slide.lookFor,
          status: "passed",
          durationMs: Date.now() - started,
        });
        console.log(`PASS ${slide.id}: ${slide.title}`);
      } catch (error) {
        await page.screenshot({
          path: path.join(output, "failure.png"),
          fullPage: true,
        });
        writeFileSync(path.join(output, "failure.html"), await page.content());
        report.slides.push({
          id: slide.id,
          title: slide.title,
          status: "failed",
          error: String(error),
          durationMs: Date.now() - started,
        });
        throw error;
      }
    }
  } finally {
    await browser.close();
    report.finishedAt = new Date().toISOString();
    report.durationMs =
      Date.parse(report.finishedAt) - Date.parse(report.startedAt);
    writeFileSync(
      path.join(output, "report.json"),
      JSON.stringify(report, null, 2) + "\n",
    );
  }
  console.log(
    `PASS ${report.slides.length} slide actions in ${(report.durationMs / 1000).toFixed(1)}s. Report: ${output}/report.json`,
  );
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

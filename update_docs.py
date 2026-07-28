with open("docs/agentic-commerce-stack-plan.md", "r") as f:
    text = f.read()

# I am just going to add a [COMPLETED] tag.
if "### Phase 3 — Identity directory and job application path" in text:
    text = text.replace("### Phase 3 — Identity directory and job application path", "### Phase 3 — Identity directory and job application path [COMPLETED]")

with open("docs/agentic-commerce-stack-plan.md", "w") as f:
    f.write(text)

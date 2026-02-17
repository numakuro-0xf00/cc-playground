# cc-playground

A playground repository for Claude Code experimentation and testing, featuring a collection of skills that extend Claude's capabilities with specialized knowledge and workflows.

## Repository Status

**Current State:** Active development environment for Claude Code skills

This repository serves as an experimental workspace for creating, testing, and refining Claude Code skills. It contains working skills and the tooling necessary to create new ones.

## What are Skills?

Skills are modular, self-contained packages that extend Claude Code's capabilities by providing:

- **Specialized workflows**: Multi-step procedures for specific domains
- **Tool integrations**: Instructions for working with specific file formats or APIs
- **Domain expertise**: Specialized knowledge, schemas, and business logic
- **Bundled resources**: Scripts, references, and assets for complex tasks

Think of skills as "onboarding guides" that transform Claude from a general-purpose agent into a specialized agent with procedural knowledge for specific tasks.

## Repository Structure

```
cc-playground/
├── CLAUDE.md           # Instructions for Claude Code when working in this repo
├── README.md           # This file
├── LICENSE             # Repository license
└── skills/             # Skills directory
    ├── skill-creator/  # Skill for creating new skills
    │   ├── SKILL.md           # Skill instructions and metadata
    │   ├── scripts/           # Skill creation and validation tools
    │   │   ├── init_skill.py      # Initialize new skill structure
    │   │   ├── quick_validate.py  # Validate skill structure
    │   │   └── package_skill.py   # Package skill for distribution
    │   └── references/        # Design pattern documentation
    │       ├── workflows.md       # Sequential/conditional patterns
    │       └── output-patterns.md # Template and example patterns
    └── naming/         # Purpose-driven naming skill (Japanese)
        └── SKILL.md           # Naming conventions and principles
```

## Available Skills

### 1. skill-creator

**Purpose:** Guide for creating effective Claude Code skills

**Location:** `skills/skill-creator/`

**Use when:** You want to create a new skill or update an existing skill that extends Claude's capabilities with specialized knowledge, workflows, or tool integrations.

**Key Features:**
- Complete skill creation workflow from understanding to packaging
- Three types of bundled resources: scripts, references, and assets
- Progressive disclosure design for efficient context management
- Validation and packaging tools

**Tools:**
- `scripts/init_skill.py <name> --path <dir>` - Initialize new skill with template structure
- `scripts/quick_validate.py <skill-dir>` - Validate skill structure and frontmatter
- `scripts/package_skill.py <skill-dir> [output-dir]` - Package skill into distributable .skill file

**Documentation:**
- `references/workflows.md` - Sequential and conditional workflow patterns
- `references/output-patterns.md` - Template and example patterns for consistent output

### 2. naming

**Purpose:** Purpose-driven naming conventions for programming (Japanese language)

**Location:** `skills/naming/`

**Use when:** You're struggling with naming classes, functions, or variables; conducting code reviews to improve naming; or refactoring code to clarify intent.

**Key Principles:**
- Purpose-driven naming (focus on "why" rather than "what")
- Maximize specificity (concrete, narrow, purpose-specific names)
- Separation of concerns (single responsibility per name)

## How to Use This Repository

### Creating a New Skill

1. **Understand the skill** with concrete examples of how it will be used
2. **Plan reusable contents** (scripts, references, assets)
3. **Initialize the skill:**
   ```bash
   skills/skill-creator/scripts/init_skill.py <skill-name> --path skills/
   ```
4. **Edit the skill** by implementing resources and writing SKILL.md
5. **Package the skill:**
   ```bash
   skills/skill-creator/scripts/package_skill.py skills/<skill-name>
   ```
6. **Iterate** based on real usage feedback

### Working with Existing Skills

Skills are automatically available to Claude Code when working in this repository. Each skill's SKILL.md contains:

- **YAML frontmatter**: Name and description (determines when skill triggers)
- **Markdown body**: Instructions and guidance for using the skill

### Skill Anatomy

Every skill consists of:

- **SKILL.md** (required): Metadata and instructions
- **scripts/** (optional): Executable code for deterministic tasks
- **references/** (optional): Documentation loaded as needed
- **assets/** (optional): Files used in output (templates, boilerplate)

## Development Approach

This is a playground environment for experimentation and prototyping. The focus is on:

- **Rapid iteration**: Test ideas quickly and refine based on usage
- **Minimal complexity**: Only add what's necessary
- **Progressive disclosure**: Keep context efficient through layered loading
- **Token efficiency**: Concise is key - Claude is already very smart

## Contributing

This is an experimental repository. When adding new skills:

1. Follow the skill creation process documented in `skill-creator`
2. Keep SKILL.md body under 500 lines
3. Use progressive disclosure for complex skills
4. Test scripts before committing
5. Validate with `quick_validate.py` before packaging

## License

See [LICENSE](LICENSE) file for details.

## Resources

- [Claude Code Documentation](https://claude.ai/code)
- Skills in this repository follow the patterns described in `skills/skill-creator/SKILL.md`

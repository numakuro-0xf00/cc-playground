# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This is a playground repository for Claude Code experimentation and testing.

## Skills

The `skills/` directory contains Claude Code skills that extend capabilities:

### skill-creator

A skill for creating new skills. Located at `skills/skill-creator/`.

**Scripts:**
- `scripts/init_skill.py <name> --path <dir>` - Initialize a new skill with template structure
- `scripts/quick_validate.py <skill-dir>` - Validate a skill's structure and frontmatter
- `scripts/package_skill.py <skill-dir> [output-dir]` - Package a skill into a distributable .skill file

**References:**
- `references/workflows.md` - Sequential and conditional workflow patterns
- `references/output-patterns.md` - Template and example patterns for consistent output

## Development Approach

This is a playground environment for experimentation and prototyping.

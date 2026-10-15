# Bumps <ApplicationDisplayVersion>/<ApplicationVersion> in CommitDesk.csproj and rolls the "## master" section of
# CHANGELOG.md into a new version section, listing merged merge/pull requests since the last tag.
# Shared by .gitlab-ci.yml (bump-version job) and .github/workflows/bump-version.yml.
#
# Usage: python build/bump_version.py <patch|minor|major|prepatch|preminor|premajor|prerelease> [preid]
# Writes the new version to bump_version.txt, and to $GITHUB_OUTPUT as "version" on GitHub Actions.
#
# Changelog entries are looked up through the API of whichever CI runs the script:
#   GitLab: "!12" references, via CI_API_V4_URL / CI_PROJECT_ID with CI_PUSH_TOKEN
#   GitHub: "#12" references, via the REST API for GITHUB_REPOSITORY with GITHUB_TOKEN
import json, os, re, subprocess, sys, urllib.request

version_type   = sys.argv[1]
preid          = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2].strip() else 'rc'
csproj_path    = 'CommitDesk/CommitDesk.csproj'
changelog_path = 'CHANGELOG.md'
on_github      = os.environ.get('GITHUB_ACTIONS') == 'true'


# Files are UTF-8 (possibly with a BOM), with CRLF line endings on Windows runners. Read them as UTF-8
# (not the Windows ANSI default, which fails on the changelog's emoji) and write them back the same way.
def read_text(path):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    newline = '\r\n' if b'\r\n' in raw else '\n'
    return raw.decode('utf-8-sig').replace('\r\n', '\n'), bom, newline


def write_text(path, text, bom, newline):
    with open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline) as f:
        f.write(text)


def get_json(url, headers):
    return json.load(urllib.request.urlopen(urllib.request.Request(url, headers=headers)))


# ── Read current version from .csproj ───────────────────────────────────
csproj, csproj_bom, csproj_nl = read_text(csproj_path)
m = re.search(r'<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>', csproj)
if not m:
    raise RuntimeError(f"Could not find <ApplicationDisplayVersion> in {csproj_path}")
current = m.group(1).strip()
print(f"Current version: {current}")

bm = re.search(r'<ApplicationVersion>([^<]+)</ApplicationVersion>', csproj)
current_build = int(bm.group(1).strip()) if bm else 0

# ── Parse version (supports X.Y.Z and X.Y.Z-pre.N) ──────────────────────
pre_match = re.match(r'^(\d+)\.(\d+)\.(\d+)-(.+?)\.(\d+)$', current)
rel_match = re.match(r'^(\d+)\.(\d+)\.(\d+)$', current)

if pre_match:
    major, minor, patch = int(pre_match.group(1)), int(pre_match.group(2)), int(pre_match.group(3))
    pre_tag, pre_num = pre_match.group(4), int(pre_match.group(5))
    is_pre = True
elif rel_match:
    major, minor, patch = int(rel_match.group(1)), int(rel_match.group(2)), int(rel_match.group(3))
    pre_tag, pre_num = None, 0
    is_pre = False
else:
    raise RuntimeError(f"Unrecognised version format: {current}")

# ── Compute new version ──────────────────────────────────────────────────
if version_type == 'patch':
    new_version = f"{major}.{minor}.{patch}" if is_pre else f"{major}.{minor}.{patch + 1}"
elif version_type == 'minor':
    new_version = f"{major}.{minor}.0" if is_pre else f"{major}.{minor + 1}.0"
elif version_type == 'major':
    new_version = f"{major}.0.0" if is_pre else f"{major + 1}.0.0"
elif version_type == 'prepatch':
    new_version = f"{major}.{minor}.{patch + 1}-{preid}.1"
elif version_type == 'preminor':
    new_version = f"{major}.{minor + 1}.0-{preid}.1"
elif version_type == 'premajor':
    new_version = f"{major + 1}.0.0-{preid}.1"
elif version_type == 'prerelease':
    if is_pre:
        new_version = f"{major}.{minor}.{patch}-{pre_tag}.{pre_num + 1}"
    else:
        new_version = f"{major}.{minor}.{patch + 1}-{preid}.1"
else:
    raise RuntimeError(f"Unknown version type: {version_type}")

new_build = current_build + 1
print(f"New version: {new_version} (build {new_build})")

# ── Update .csproj ───────────────────────────────────────────────────────
# ApplicationVersion backs the Android versionCode and the Apple CFBundleVersion: it must increase on every release.
csproj = re.sub(r'<ApplicationDisplayVersion>[^<]+</ApplicationDisplayVersion>',
                f'<ApplicationDisplayVersion>{new_version}</ApplicationDisplayVersion>', csproj)
csproj = re.sub(r'<ApplicationVersion>[^<]+</ApplicationVersion>',
                f'<ApplicationVersion>{new_build}</ApplicationVersion>', csproj)
write_text(csproj_path, csproj, csproj_bom, csproj_nl)
print(f"Updated {csproj_path}")

# ── Update CHANGELOG.md ──────────────────────────────────────────────────
if not os.path.exists(changelog_path):
    print("No CHANGELOG.md found, creating one")
    write_text(changelog_path, '# Changelog\n\n', False, csproj_nl)

changelog, changelog_bom, changelog_nl = read_text(changelog_path)

try:
    latest_tag = subprocess.check_output(['git', 'describe', '--tags', '--abbrev=0'], text=True).strip()
    print(f"Latest tag: {latest_tag}")
    commit_range = f"{latest_tag}..HEAD"
except subprocess.CalledProcessError:
    print("No previous tags found, using all commits")
    commit_range = 'HEAD'

try:
    # Full messages: GitLab merge commits reference the MR in the body ("See merge request group/project!12");
    # GitHub uses "Merge pull request #12" or "Title (#12)" for squash merges.
    commits = subprocess.check_output(['git', 'log', commit_range, '--format=%B'], text=True, encoding='utf-8')
except subprocess.CalledProcessError:
    commits = ''

prefix = '#' if on_github else '!'
numbers = list(dict.fromkeys(re.findall(re.escape(prefix) + r'(\d+)\b', commits)))
missing = [n for n in numbers if f'{prefix}{n}' not in changelog]
print(f"Found {len(missing)} new {'pull' if on_github else 'merge'} requests to add to changelog")

missing_entries = []
for number in missing:
    try:
        if on_github:
            headers = {'Accept': 'application/vnd.github+json', 'User-Agent': 'bump-version'}
            if os.environ.get('GITHUB_TOKEN'):   # an empty bearer token is rejected even for public repos
                headers['Authorization'] = f"Bearer {os.environ['GITHUB_TOKEN']}"
            pr = get_json(f"https://api.github.com/repos/{os.environ['GITHUB_REPOSITORY']}/pulls/{number}", headers)
            author, title, url = pr['user']['login'], pr['title'], pr['html_url']
        else:
            mr = get_json(f"{os.environ['CI_API_V4_URL']}/projects/{os.environ['CI_PROJECT_ID']}/merge_requests/{number}",
                          {'PRIVATE-TOKEN': os.environ.get('CI_PUSH_TOKEN', '')})
            author, title, url = mr['author']['username'], mr['title'], mr['web_url']
        if re.search(r'bot|dependabot|renovate', author, re.I):
            continue
        entry = f"- {title} ([{prefix}{number}]({url})) (@{author})"
        missing_entries.append(entry)
        print(f"Added: {entry}")
    except Exception as e:
        print(f"Could not fetch {prefix}{number}: {e}")

# Replace "## master" with new version heading
changelog = changelog.replace('## master', f'## {new_version}', 1)
# Remove placeholder lines
changelog = changelog.replace('- _...Add new stuff here..._\n', '')

# Prepend fresh master section
master_section = '\n'.join([
    '## master',
    '### ✨ Features and improvements',
    '- _...Add new stuff here..._',
    '',
    '### 🐞 Bug fixes',
    '- _...Add new stuff here..._',
    '',
    '',
])

title_match = re.match(r'^(# .+?\n\n)', changelog)
if title_match:
    title = title_match.group(1)
    rest = changelog[len(title):]
else:
    title = '# Changelog\n\n'
    rest = changelog

# Insert missing entries into the Bug fixes block of the new version section
if missing_entries:
    bug_fix_pattern = re.compile(
        r'^(## [^\n]+\n### ✨ Features and improvements\n(?:.*\n)*?### 🐞 Bug fixes\n)',
        re.MULTILINE)
    bf_match = bug_fix_pattern.search(rest)
    if bf_match:
        insert_at = bf_match.end()
        rest = rest[:insert_at] + '\n' + '\n'.join(missing_entries) + '\n' + rest[insert_at:]
    else:
        rest = rest + '\n' + '\n'.join(missing_entries) + '\n'

changelog = title + master_section + rest
write_text(changelog_path, changelog, changelog_bom, changelog_nl)
print("Updated CHANGELOG.md")

open('bump_version.txt', 'w', encoding='utf-8').write(new_version)
if os.environ.get('GITHUB_OUTPUT'):
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as f:
        f.write(f"version={new_version}\n")

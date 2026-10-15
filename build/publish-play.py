# Uploads an Android App Bundle to Google Play and releases it on a track, through the Google Play Developer API.
# Shared by .gitlab-ci.yml (publish-play job) and .github/workflows/publish-play.yml.
#
# Usage:
#   python build/publish-play.py --aab CommitDesk.aab --version 0.5.0 [--track internal] [--status completed]
#
# Needs: pip install google-api-python-client google-auth
#
# The service account key (JSON, from Google Cloud, invited to the app in Play Console › Users and permissions with
# "Release to testing tracks" and, for production, "Release to production") comes from the environment:
#   PLAY_SERVICE_ACCOUNT_JSON_BASE64  the key file, base64-encoded (GitLab: a masked variable can't hold raw JSON)
#   PLAY_SERVICE_ACCOUNT_JSON         or the key file's JSON itself (a GitHub secret can)
#
# Google only accepts API uploads for an app that already exists in Play Console and has had its first bundle
# uploaded there by hand (which also sets up Play App Signing). See docs/maintainers.md.
import argparse, base64, json, os, re, sys

PACKAGE = 'net.techidiots.commitdesk'
NOTES_LIMIT = 500   # Play's limit per language


def service_account_info():
    encoded = os.environ.get('PLAY_SERVICE_ACCOUNT_JSON_BASE64', '').strip()
    raw = os.environ.get('PLAY_SERVICE_ACCOUNT_JSON', '').strip()
    if encoded:
        raw = base64.b64decode(encoded).decode('utf-8')
    if not raw:
        sys.exit('PLAY_SERVICE_ACCOUNT_JSON_BASE64 (or PLAY_SERVICE_ACCOUNT_JSON) is not set.')
    try:
        return json.loads(raw)
    except json.JSONDecodeError as e:
        sys.exit(f'The Play service account key is not valid JSON: {e}')


def release_notes(version):
    """The version's section of CHANGELOG.md as plain text, cut to Play's limit."""
    try:
        text = open('CHANGELOG.md', encoding='utf-8-sig').read().replace('\r\n', '\n')
    except FileNotFoundError:
        return f'Version {version}'
    m = re.search(rf'^## {re.escape(version)}\n(.*?)(?=^## |\Z)', text, re.S | re.M)
    if not m:
        return f'Version {version}'
    lines = []
    for line in m.group(1).splitlines():
        line = line.strip()
        if not line or line.startswith('- _...'):
            continue
        line = re.sub(r'^#+\s*', '', line)                      # headings
        line = re.sub(r'[✨\U0001F41E]\s*', '', line)       # the changelog's heading emoji
        line = re.sub(r'\*\*(.+?)\*\*', r'\1', line)             # bold
        line = re.sub(r'`([^`]+)`', r'\1', line)                 # code
        line = re.sub(r'\[([^\]]+)\]\([^)]+\)', r'\1', line)     # links
        line = re.sub(r'^- ', '• ', line)
        lines.append(line)
    # Drop headings left with nothing under them
    kept = [l for i, l in enumerate(lines)
            if l.startswith('• ') or (i + 1 < len(lines) and lines[i + 1].startswith('• '))]
    notes = '\n'.join(kept) or f'Version {version}'
    if len(notes) > NOTES_LIMIT:
        notes = notes[:NOTES_LIMIT - 1].rsplit(' ', 1)[0] + '…'
    return notes


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--aab', required=True, help='the signed .aab to upload')
    p.add_argument('--version', required=True, help='the app version (e.g. 0.5.0), for the release name and notes')
    p.add_argument('--track', default='internal', help='internal, alpha (closed), beta (open) or production')
    p.add_argument('--status', default='completed', help='completed, or draft to finish the release in Play Console')
    p.add_argument('--package', default=PACKAGE)
    args = p.parse_args()

    from google.oauth2 import service_account
    from googleapiclient.discovery import build
    from googleapiclient.errors import HttpError
    from googleapiclient.http import MediaFileUpload

    credentials = service_account.Credentials.from_service_account_info(
        service_account_info(), scopes=['https://www.googleapis.com/auth/androidpublisher'])
    edits = build('androidpublisher', 'v3', credentials=credentials, cache_discovery=False).edits()

    notes = release_notes(args.version)
    print(f'Release notes ({len(notes)} characters):\n{notes}\n')

    def publish(status):
        edit_id = edits.insert(packageName=args.package, body={}).execute()['id']
        print(f'Uploading {args.aab}…')
        bundle = edits.bundles().upload(
            packageName=args.package, editId=edit_id,
            media_body=MediaFileUpload(args.aab, mimetype='application/octet-stream', resumable=True)).execute()
        code = bundle['versionCode']
        print(f'Uploaded version code {code}.')
        edits.tracks().update(packageName=args.package, editId=edit_id, track=args.track, body={
            'track': args.track,
            'releases': [{
                'name': args.version,
                'versionCodes': [str(code)],
                'status': status,
                'releaseNotes': [{'language': 'en-US', 'text': notes}],
            }],
        }).execute()
        edits.commit(packageName=args.package, editId=edit_id).execute()
        print(f'Released {args.version} (version code {code}) on the {args.track} track as {status}.')

    try:
        publish(args.status)
    except HttpError as e:
        message = str(e)
        if 'draft app' in message and args.status != 'draft':
            # An app that has never been published only accepts draft releases; finish those in Play Console
            print('The app has not been published yet, so Play only accepts a draft release. Retrying as a draft;\n'
                  'roll it out from Play Console › Testing (or Production) › the release.')
            publish('draft')
        elif 'Package not found' in message or 'not found' in message.lower() and args.package in message:
            sys.exit(f'Play has no app {args.package} that this service account can reach. Create the app in Play '
                     'Console, upload the first bundle there by hand, and invite the service account '
                     '(see docs/maintainers.md).\n' + message)
        elif 'Version code' in message and 'already been used' in message:
            sys.exit('Play already has this version code. Bump the version (ApplicationVersion) and release again.\n'
                     + message)
        else:
            raise


if __name__ == '__main__':
    main()

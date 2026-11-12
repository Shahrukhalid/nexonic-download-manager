# Profile listers used by Nexonic's Profile Batch (loaded with --plugin-dirs). They only read what a site shows to
# visitors who are not signed in: no login, no cookies, nothing bypassed. Each lister returns links to single posts,
# which the engine's own extractors then download. When a site shows only part of a profile to visitors, the list
# stops there and says so.
import json
import re

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.networking.exceptions import HTTPError
from yt_dlp.utils import ExtractorError, int_or_none, traverse_obj

N = 'Nexonic: '  # the app shows messages starting with this as they are

_RESERVED_IG = ('p', 'reel', 'reels', 'tv', 'stories', 'explore', 'accounts', 'direct', 'about', 'legal', 'developer',
                'api', 'graphql', 'web', 'challenge', 'emails', 'session', 'privacy', 'terms')


def _title(text, fallback):
    text = re.sub(r'\s+', ' ', text or '').strip()
    return (text[:120] + '…') if len(text) > 120 else (text or fallback)


class NexonicInstagramProfileIE(InfoExtractor):
    IE_NAME = 'instagram:profile'
    IE_DESC = 'Instagram profile (public posts shown to visitors)'
    _VALID_URL = r'https?://(?:www\.)?instagram\.com/(?!(?:%s)/)(?P<id>[A-Za-z0-9._]{1,30})/?(?:reels/?|videos/?)?(?:$|[?#])' % '|'.join(_RESERVED_IG)
    _APP_ID = '936619743392459'  # the id Instagram's own web page sends; public, not an account

    def _headers(self, user):
        return {'X-IG-App-ID': self._APP_ID, 'X-ASBD-ID': '129477', 'X-Requested-With': 'XMLHttpRequest',
                'Referer': f'https://www.instagram.com/{user}/', 'Accept': '*/*'}

    def _api(self, path, user, note, query=None):
        try:
            return self._download_json(f'https://www.instagram.com/api/v1/{path}', user, note, query=query,
                                       headers=self._headers(user))
        except ExtractorError as e:
            if isinstance(e.cause, HTTPError) and e.cause.status in (401, 403):
                return None
            if isinstance(e.cause, HTTPError) and e.cause.status == 429:
                raise ExtractorError(N + 'Instagram is limiting requests from this network right now. Try again in a few minutes.', expected=True)
            if isinstance(e.cause, HTTPError) and e.cause.status == 404:
                return None  # Instagram answers 404 both for missing profiles and when it no longer shows profiles this way
            raise

    @staticmethod
    def _entry_from_node(n):
        typename = n.get('__typename') or ''
        videos = n.get('is_video') or n.get('product_type') in ('clips', 'igtv') or any(
            traverse_obj(n, ('edge_sidecar_to_children', 'edges', ..., 'node', 'is_video')) or [])
        if not videos or not n.get('shortcode'):
            return None
        kind = 'reel' if n.get('product_type') == 'clips' else 'p'
        return {
            '_type': 'url', 'ie_key': 'Instagram', 'id': n['shortcode'],
            'url': f'https://www.instagram.com/{kind}/{n["shortcode"]}/',
            'title': _title(traverse_obj(n, ('edge_media_to_caption', 'edges', 0, 'node', 'text')), f'Instagram {typename or "video"} {n["shortcode"]}'),
            'duration': n.get('video_duration'), 'timestamp': int_or_none(n.get('taken_at_timestamp')),
        }

    @staticmethod
    def _entry_from_item(it):
        code = it.get('code')
        videos = it.get('media_type') == 2 or any(m.get('media_type') == 2 for m in it.get('carousel_media') or [])
        if not videos or not code:
            return None
        kind = 'reel' if it.get('product_type') == 'clips' else 'p'
        return {
            '_type': 'url', 'ie_key': 'Instagram', 'id': code, 'url': f'https://www.instagram.com/{kind}/{code}/',
            'title': _title(traverse_obj(it, ('caption', 'text')), f'Instagram video {code}'),
            'duration': it.get('video_duration'), 'timestamp': int_or_none(it.get('taken_at')),
        }

    def _real_extract(self, url):
        user = self._match_id(url)
        info = self._api('users/web_profile_info/', user, 'Reading the public profile', {'username': user})
        u = traverse_obj(info, ('data', 'user'))
        if not u:
            raise ExtractorError(N + 'Instagram did not let Nexonic read this profile without signing in, and Nexonic does not '
                                 'sign in. To pick its videos anyway, open the profile in your browser, right-click the page and '
                                 'choose "Download all links with Nexonic".', expected=True)
        if u.get('is_private'):
            raise ExtractorError(N + 'This Instagram profile is private. Only its approved followers can see its videos.', expected=True)
        timeline = u.get('edge_owner_to_timeline_media') or {}
        total = timeline.get('count')

        def entries():
            seen, shown = set(), 0
            for n in traverse_obj(timeline, ('edges', ..., 'node')) + traverse_obj(u, ('edge_felix_video_timeline', 'edges', ..., 'node')):
                shown += 1
                e = self._entry_from_node(n)
                if e and e['id'] not in seen:
                    seen.add(e['id'])
                    yield e
            cursor = traverse_obj(timeline, ('page_info', 'end_cursor'))
            more = traverse_obj(timeline, ('page_info', 'has_next_page'))
            page = 1
            while more and cursor:
                page += 1
                feed = self._api(f'feed/user/{u["id"]}/', user, f'Reading page {page}', {'count': 12, 'max_id': cursor})
                items = (feed or {}).get('items')
                if not items:
                    self.report_warning(N + f'Instagram shows visitors who are not signed in only the latest {shown} of {total or "its"} posts '
                                        'of this profile; the rest needs a signed-in account, which Nexonic does not use.')
                    return
                for it in items:
                    shown += 1
                    e = self._entry_from_item(it)
                    if e and e['id'] not in seen:
                        seen.add(e['id'])
                        yield e
                more, cursor = feed.get('more_available'), feed.get('next_max_id')

        return self.playlist_result(entries(), user, u.get('full_name') or user, playlist_count=total)


class NexonicRedditListingIE(InfoExtractor):
    IE_NAME = 'reddit:listing'
    IE_DESC = 'Reddit user or community (public posts)'
    _VALID_URL = r'https?://(?:(?:www|old|new)\.)?reddit\.com/(?P<kind>u|user|r)/(?P<id>[\w-]+)(?:/(?P<tab>submitted|hot|new|top|rising|videos))?/?(?:$|[?#])'

    def _real_extract(self, url):
        kind, name, tab = self._match_valid_url(url).group('kind', 'id', 'tab')
        path = f'user/{name}/submitted' if kind != 'r' else f'r/{name}/{tab if tab in ("hot", "new", "top", "rising") else "new"}'

        def entries():
            after = None
            for page in range(1, 51):
                try:
                    data = self._download_json(f'https://www.reddit.com/{path}.json', name, f'Reading page {page}',
                                               query={'limit': 100, 'raw_json': 1, **({'after': after} if after else {})})
                except ExtractorError as e:
                    if isinstance(e.cause, HTTPError) and e.cause.status in (403, 429):
                        raise ExtractorError(N + 'Reddit refused the request from this network (it blocks some networks and '
                                             'servers for visitors who are not signed in).', expected=True)
                    if isinstance(e.cause, HTTPError) and e.cause.status == 404:
                        raise ExtractorError(N + 'This Reddit page does not exist or is not public.', expected=True)
                    raise
                for p in traverse_obj(data, ('data', 'children', ..., 'data')):
                    if p.get('is_video') or p.get('post_hint') in ('hosted:video', 'rich:video') or 'v.redd.it' in (p.get('url') or ''):
                        yield {'_type': 'url', 'ie_key': 'Reddit', 'id': p.get('id'),
                               'url': 'https://www.reddit.com' + p['permalink'], 'title': _title(p.get('title'), p.get('id')),
                               'duration': traverse_obj(p, ('media', 'reddit_video', 'duration')),
                               'timestamp': int_or_none(p.get('created_utc'))}
                after = traverse_obj(data, ('data', 'after'))
                if not after:
                    return

        return self.playlist_result(entries(), f'{kind}-{name}', ('r/' if kind == 'r' else 'u/') + name)


class NexonicFacebookPageIE(InfoExtractor):
    IE_NAME = 'facebook:page'
    IE_DESC = 'Facebook page or profile (videos and reels shown to visitors)'
    _VALID_URL = r'https?://(?:www\.|m\.)?facebook\.com/(?!(?:watch|reel|videos|groups|events|share|story\.php|photo|permalink\.php|login|marketplace|gaming)\b)(?P<id>(?:profile\.php\?id=\d+)|[\w.-]+)(?:/(?P<tab>videos|reels|reels_tab|videos_by))?/?(?:$|[?#&])'

    def _real_extract(self, url):
        page = self._match_id(url)
        base = f'https://www.facebook.com/{page}'
        found, titles = [], {}
        for tab in ('reels', 'videos'):
            target = f'{base}&sk={tab}' if page.startswith('profile.php') else f'{base}/{tab}/'
            try:
                html = self._download_webpage(target, page, f'Reading the {tab} page', headers={'Accept-Language': 'en-US,en;q=0.9', 'Sec-Fetch-Mode': 'navigate'})
            except ExtractorError:
                continue
            for vid in re.findall(r'(?:\\?/reel\\?/|/videos/(?:[^/"\\]+\\?/)?|"video_id":"|\\"video_id\\":\\")(\d{8,})', html):
                if vid not in found:
                    found.append(vid)
            for vid, t in re.findall(r'"video_id":"(\d{8,})".{0,400}?"title":\{"text":"([^"]{1,200})"', html):
                titles.setdefault(vid, json.loads(f'"{t}"'))
        if not found:
            raise ExtractorError(N + 'Facebook did not show any videos of this page to a visitor who is not signed in. '
                                 'Nexonic does not sign in, so it cannot list them.', expected=True)
        self.report_warning(N + 'Facebook shows visitors who are not signed in only the most recent videos of a page.')
        return self.playlist_result(
            ({'_type': 'url', 'ie_key': 'Facebook', 'id': v, 'url': f'https://www.facebook.com/reel/{v}',
              'title': _title(titles.get(v), f'Facebook video {v}')} for v in found), page, page)


class NexonicXProfileIE(InfoExtractor):
    IE_NAME = 'x:profile'
    IE_DESC = 'X (Twitter) profile (the public timeline shown in embeds)'
    _VALID_URL = r'https?://(?:www\.|mobile\.)?(?:x|twitter)\.com/(?!(?:i|home|explore|search|settings|messages|notifications|hashtag|intent|share)/?(?:$|[/?#]))(?P<id>\w{1,15})(?:/media|/videos)?/?(?:$|[?#])'

    def _real_extract(self, url):
        user = self._match_id(url)
        # Only X's own public timeline (the one embedded on other sites); no outside services, no sign-in.
        html = self._download_webpage(f'https://syndication.twitter.com/srv/timeline-profile/screen-name/{user}', user,
                                      'Reading the public timeline', fatal=False, expected_status=(401, 403, 429)) or ''
        data = self._search_json(r'<script[^>]+id="__NEXT_DATA__"[^>]*>', html, 'timeline', user, default={})
        tweets = traverse_obj(data, ('props', 'pageProps', 'timeline', 'entries', ..., 'content', 'tweet')) or []
        if not tweets:
            raise ExtractorError(N + 'X did not show any posts of this profile to a visitor who is not signed in right now. '
                                 'Nexonic does not sign in, so it cannot list them.', expected=True)

        def entries():
            for t in tweets:
                media = traverse_obj(t, ('extended_entities', 'media', ...)) or traverse_obj(t, ('entities', 'media', ...)) or []
                if any(m.get('type') in ('video', 'animated_gif') for m in media):
                    tid = t.get('id_str')
                    yield {'_type': 'url', 'ie_key': 'Twitter', 'id': tid, 'url': f'https://x.com/{user}/status/{tid}',
                           'title': _title(t.get('full_text') or t.get('text'), f'X video {tid}')}

        self.report_warning(N + 'X shows visitors who are not signed in only the latest posts of a profile.')
        return self.playlist_result(entries(), user, user)

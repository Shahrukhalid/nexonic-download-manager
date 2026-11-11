# Offline checks of the profile listers in engine/nexonic (run: python3 scripts/test-profile-listers.py).
import os, re, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'engine', 'nexonic'))
from yt_dlp_plugins.extractor.nexonic_profiles import (NexonicFacebookPageIE as FB, NexonicInstagramProfileIE as IG,
                                                       NexonicRedditListingIE as RD, NexonicXProfileIE as X)


def m(ie, url):
    return re.match(ie._VALID_URL, url) is not None


class Profiles(unittest.TestCase):
    def test_urls(self):
        for ie, yes, no in [
            (IG, ['https://www.instagram.com/alexvkino/', 'https://instagram.com/nasa', 'https://www.instagram.com/nasa/reels/'],
             ['https://www.instagram.com/p/C1abc/', 'https://www.instagram.com/reel/C1abc/', 'https://www.instagram.com/explore/tags/x/']),
            (RD, ['https://www.reddit.com/user/spez/', 'https://reddit.com/r/videos', 'https://www.reddit.com/r/videos/top/', 'https://www.reddit.com/u/x/submitted'],
             ['https://www.reddit.com/r/videos/comments/abc/title/']),
            (FB, ['https://www.facebook.com/NASA/', 'https://www.facebook.com/NASA/videos', 'https://www.facebook.com/NASA/reels/', 'https://www.facebook.com/profile.php?id=100064'],
             ['https://www.facebook.com/watch/?v=1', 'https://www.facebook.com/reel/123456789', 'https://www.facebook.com/NASA/videos/123456789/']),
            (X, ['https://x.com/NASA', 'https://twitter.com/NASA/media', 'https://x.com/NASA/'],
             ['https://x.com/NASA/status/1', 'https://x.com/i/status/1', 'https://x.com/home', 'https://x.com/search?q=a']),
        ]:
            for u in yes:
                self.assertTrue(m(ie, u), u)
            for u in no:
                self.assertFalse(m(ie, u), u)

    def test_instagram_entries(self):
        video = {'__typename': 'GraphVideo', 'shortcode': 'AAA', 'is_video': True, 'product_type': 'clips', 'taken_at_timestamp': 1700000000,
                 'video_duration': 12.5, 'edge_media_to_caption': {'edges': [{'node': {'text': 'Hello\nworld'}}]}}
        e = IG._entry_from_node(video)
        self.assertEqual((e['url'], e['title'], e['duration'], e['timestamp']), ('https://www.instagram.com/reel/AAA/', 'Hello world', 12.5, 1700000000))
        self.assertIsNone(IG._entry_from_node({'shortcode': 'B', 'is_video': False, '__typename': 'GraphImage'}))
        carousel = {'shortcode': 'C', '__typename': 'GraphSidecar', 'edge_sidecar_to_children': {'edges': [{'node': {'is_video': False}}, {'node': {'is_video': True}}]}}
        self.assertEqual(IG._entry_from_node(carousel)['url'], 'https://www.instagram.com/p/C/')
        item = {'code': 'D', 'media_type': 8, 'carousel_media': [{'media_type': 2}], 'taken_at': 5, 'caption': None}
        self.assertEqual(IG._entry_from_item(item)['url'], 'https://www.instagram.com/p/D/')
        self.assertIsNone(IG._entry_from_item({'code': 'E', 'media_type': 1}))


if __name__ == '__main__':
    unittest.main()

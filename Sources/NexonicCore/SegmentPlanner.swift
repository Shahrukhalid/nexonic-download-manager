import Foundation

public enum SegmentPlanner {
    public static let minSegment: Int64 = 512 * 1024
    public static let minSplit: Int64 = 1 << 20

    /// Equal segments of at least 512 KB, between 1 and 32 of them.
    public static func plan(size: Int64?, connections: Int, minSegment: Int64 = SegmentPlanner.minSegment) -> [Segment] {
        guard let size, size > 0 else { return [Segment(id: 0, start: 0, end: -1)] }
        let byMin = Int(max(1, size / max(1, minSegment)))
        let n = max(1, min(connections, 32, byMin))
        let base = size / Int64(n)
        var out: [Segment] = []
        var start: Int64 = 0
        for i in 0..<n {
            let end = i == n - 1 ? size - 1 : start + base - 1
            out.append(Segment(id: i, start: start, end: end))
            start = end + 1
        }
        return out
    }

    /// Finds the unfinished segment with the most bytes left and splits it in half.
    /// Returns the index of the shortened segment, its new end, and the new segment for the second half.
    public static func splitLargest(_ segments: [Segment], nextID: Int, minSegment: Int64 = SegmentPlanner.minSegment) -> (index: Int, newEnd: Int64, added: Segment)? {
        var best: Int?
        var bestLeft: Int64 = 0
        for (i, s) in segments.enumerated() where s.end >= 0 && !s.isComplete && s.remaining > bestLeft {
            best = i
            bestLeft = s.remaining
        }
        guard let i = best, bestLeft >= max(minSplit, 2 * minSegment) else { return nil }
        let s = segments[i]
        let position = s.start + s.written
        let mid = position + bestLeft / 2
        return (i, mid - 1, Segment(id: nextID, start: mid, end: s.end))
    }
}

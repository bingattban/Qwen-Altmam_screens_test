-- V017 additive native attendance draft evidence. Applied only by the canonical
-- hash-pinned migration runner after the mandatory encrypted rescue.
-- Existing draft rows deliberately retain NULL evidence; do not invent dates/mode.
ALTER TABLE AttendanceDrafts ADD COLUMN RangeStartDate TEXT NULL;
ALTER TABLE AttendanceDrafts ADD COLUMN RangeEndDate TEXT NULL;
ALTER TABLE AttendanceDrafts ADD COLUMN RangeIsInitial INTEGER NULL
CHECK (
  CASE
    WHEN RangeStartDate IS NULL AND RangeEndDate IS NULL AND RangeIsInitial IS NULL THEN 1
    WHEN RangeStartDate IS NOT NULL AND RangeEndDate IS NOT NULL AND RangeIsInitial IS NOT NULL
      AND TargetStatusId IN ('STS-LEAVE','STS-PERMISSION')
      AND typeof(RangeIsInitial) = 'integer' AND RangeIsInitial IN (0,1)
      AND length(RangeStartDate) = 10 AND length(RangeEndDate) = 10
      AND RangeStartDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
      AND RangeEndDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
      AND substr(RangeStartDate,1,4) <> '0000' AND substr(RangeEndDate,1,4) <> '0000'
      AND date(RangeStartDate,'+0 days') IS RangeStartDate
      AND date(RangeEndDate,'+0 days') IS RangeEndDate
      AND RangeStartDate = BusinessDate
      AND RangeEndDate >= RangeStartDate
      AND julianday(RangeEndDate) - julianday(RangeStartDate) <= 365
    THEN 1
    ELSE 0
  END = 1
);

-- Native completed draft evidence is append-only. A legacy upsert must not
-- silently change selected people/status/date while retaining stale range mode.
CREATE TRIGGER TR_AttendanceDrafts_CompleteRangeImmutable
BEFORE UPDATE ON AttendanceDrafts
WHEN OLD.RangeIsInitial IS NOT NULL
BEGIN
  SELECT RAISE(ABORT,'Complete attendance draft range evidence is immutable');
END;

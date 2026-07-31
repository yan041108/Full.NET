CREATE TABLE IF NOT EXISTS fn_sample_item
(
    Id BINARY(16) NOT NULL,
    CONSTRAINT PK_fn_sample_item PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELIMITER $$
CREATE PROCEDURE fn_sample_probe()
BEGIN
    DECLARE vActualColumns varchar(512);
    SET vActualColumns = NULL;
    IF
       vActualColumns IS NULL
       OR vActualColumns = '' THEN
        SET vActualColumns = NULL;
    END IF;
END$$
DELIMITER ;

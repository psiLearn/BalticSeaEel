CREATE UNIQUE INDEX IF NOT EXISTS idx_vocabulary_topic_terms
    ON vocabulary (topic, language1, language2);

INSERT INTO vocabulary (topic, language1, language2, example)
VALUES ('Maison','la cave','der Keller','Il range le vin dans la cave.')
ON CONFLICT (topic, language1, language2) DO NOTHING;

INSERT INTO vocabulary (topic, language1, language2, example)
VALUES ('Nourriture','le chocolat','die Schokolade','Elle mange du chocolat chaud.')
ON CONFLICT (topic, language1, language2) DO NOTHING;

INSERT INTO vocabulary (topic, language1, language2, example)
VALUES ('Animaux','le renard','der Fuchs','Le renard traverse la forêt la nuit.')
ON CONFLICT (topic, language1, language2) DO NOTHING;
